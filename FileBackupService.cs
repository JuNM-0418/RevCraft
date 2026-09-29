using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RevCraft
{
    public class TrackedVersionEntry
    {
        public int StepIndex { get; set; }
        public string FileGuid { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string CurrentPath { get; set; } = string.Empty;
        public string Status { get; set; } = "Active"; // "Active", "Moved", "Deleted"
        public string FileName => Path.GetFileName(CurrentPath);
        public string Directory => Path.GetDirectoryName(CurrentPath) ?? string.Empty;
        public bool Exists => Status != "Deleted" && File.Exists(CurrentPath);
        public long FileSize => Exists ? new FileInfo(CurrentPath).Length : 0;
        public DateTime? LastModified => Exists ? File.GetLastWriteTime(CurrentPath) : null;
        public string DisplayVersion => $"이전 버전 #{StepIndex}";

        public string StatusDisplay => Status switch
        {
            "Active" => "✔ 정상",
            "Moved" => "🔄 위치 이동됨",
            "Deleted" => "✖ 삭제됨",
            _ => Status
        };
    }

    public class VersionTrailerMeta
    {
        public string FileGuid { get; set; } = string.Empty;
        public string? ParentGuid { get; set; }
        public string ParentPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<TrackedVersionEntry> HistoryEntries { get; set; } = new();
    }

    public class FileInspectResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public string? FileGuid { get; set; }
        public List<TrackedVersionEntry> PreviousVersions { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class FileProcessResult
    {
        public bool Success { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public long SourceSize { get; set; }
        public long TargetSize { get; set; }
        public string HexAppended { get; set; } = string.Empty;
        public int Version { get; set; }
        public string? ErrorMessage { get; set; }
        public List<TrackedVersionEntry> PreviousVersions { get; set; } = new();
    }

    public static class FileBackupService
    {
        private const int LengthHeaderHexDigits = 8;

        private static readonly Regex FilePatternRegex = new Regex(
            @"^★\d{4}\.\d{2}\.\d{2}\s+(?<baseName>.+)v(?<ver>\d+)$",
            RegexOptions.Compiled);

        /// <summary>
        /// 파일을 변경하지 않고 파일 정보 및 이전 버전 체인 목록을 검사/조회합니다.
        /// </summary>
        public static FileInspectResult InspectFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new FileInspectResult
                {
                    Success = false,
                    FilePath = filePath,
                    ErrorMessage = "파일이 존재하지 않습니다."
                };
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                var meta = ReadTrailerMeta(filePath);
                var history = meta?.HistoryEntries ?? TracePreviousVersionsFallback(filePath);

                // 현재 시점의 실제 파일 존재/이동 상태 최신화
                RefreshEntriesStatus(history, Path.GetDirectoryName(filePath));

                return new FileInspectResult
                {
                    Success = true,
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime,
                    FileGuid = meta?.FileGuid,
                    PreviousVersions = history
                };
            }
            catch (Exception ex)
            {
                return new FileInspectResult
                {
                    Success = false,
                    FilePath = filePath,
                    ErrorMessage = ex.Message
                };
            }
        }

        public static (string targetPath, int version) GenerateTargetPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);

            string today = DateTime.Now.ToString("yyyy.MM.dd");

            var match = FilePatternRegex.Match(fileNameWithoutExt);

            string baseName;
            int version;

            if (match.Success)
            {
                baseName = match.Groups["baseName"].Value;
                if (int.TryParse(match.Groups["ver"].Value, out int currentVer))
                {
                    version = currentVer + 1;
                }
                else
                {
                    version = 1;
                }
            }
            else
            {
                baseName = fileNameWithoutExt;
                version = 1;
            }

            string newFileName = $"★{today} {baseName}v{version}{ext}";
            string targetPath = Path.Combine(directory, newFileName);

            while (File.Exists(targetPath))
            {
                version++;
                newFileName = $"★{today} {baseName}v{version}{ext}";
                targetPath = Path.Combine(directory, newFileName);
            }

            return (targetPath, version);
        }

        public static FileProcessResult ProcessFile(string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                return new FileProcessResult
                {
                    Success = false,
                    SourcePath = sourcePath,
                    ErrorMessage = "파일이 존재하지 않습니다."
                };
            }

            try
            {
                var (targetPath, version) = GenerateTargetPath(sourcePath);

                // 원본 파일의 기존 메타데이터 읽기
                var sourceMeta = ReadTrailerMeta(sourcePath);
                string parentGuid = sourceMeta?.FileGuid ?? Guid.NewGuid().ToString("N");

                // 이전 버전들의 이력 목록 구축 및 상태 갱신 (이동/삭제 감지)
                var newHistoryEntries = new List<TrackedVersionEntry>();

                // 1) 직전 부모 파일 등록 (#1)
                newHistoryEntries.Add(new TrackedVersionEntry
                {
                    StepIndex = 1,
                    FileGuid = parentGuid,
                    OriginalPath = sourcePath,
                    CurrentPath = sourcePath,
                    Status = "Active"
                });

                // 2) 그 이전 세대의 히스토리가 있다면 StepIndex를 올려서 포함하고 이동/삭제 상태 확인
                if (sourceMeta?.HistoryEntries != null)
                {
                    foreach (var oldEntry in sourceMeta.HistoryEntries)
                    {
                        var cloned = new TrackedVersionEntry
                        {
                            StepIndex = oldEntry.StepIndex + 1,
                            FileGuid = oldEntry.FileGuid,
                            OriginalPath = oldEntry.OriginalPath,
                            CurrentPath = oldEntry.CurrentPath,
                            Status = oldEntry.Status
                        };
                        newHistoryEntries.Add(cloned);
                    }
                }
                else
                {
                    // 메타데이터가 없는 기존 파일 체인인 경우 fallback 추적
                    var fallbackList = TracePreviousVersionsFallback(sourcePath);
                    foreach (var fb in fallbackList)
                    {
                        fb.StepIndex++;
                        newHistoryEntries.Add(fb);
                    }
                }

                // 3) 이전 버전 파일들이 다른 위치로 이동되었는지/삭제되었는지 검색 및 상태 반영
                RefreshEntriesStatus(newHistoryEntries, Path.GetDirectoryName(targetPath));

                // 4) 새 파일의 메타데이터 구성
                string newFileGuid = Guid.NewGuid().ToString("N");
                var newMeta = new VersionTrailerMeta
                {
                    FileGuid = newFileGuid,
                    ParentGuid = parentGuid,
                    ParentPath = sourcePath,
                    CreatedAt = DateTime.Now,
                    HistoryEntries = newHistoryEntries
                };

                // 5) JSON 직렬화 후 16진수 문자열로 변환
                string jsonMeta = JsonSerializer.Serialize(newMeta);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonMeta);
                string hexPayload = Convert.ToHexString(jsonBytes);
                string hexLength = hexPayload.Length.ToString("X8");
                string fullHexData = hexPayload + hexLength;

                byte[] bytesToAppend = Encoding.ASCII.GetBytes(fullHexData);

                // 6) 대상 경로로 파일 복사 후 트레일러 추가
                File.Copy(sourcePath, targetPath, overwrite: false);

                using (var fs = new FileStream(targetPath, FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytesToAppend, 0, bytesToAppend.Length);
                }

                var srcInfo = new FileInfo(sourcePath);
                var tgtInfo = new FileInfo(targetPath);

                return new FileProcessResult
                {
                    Success = true,
                    SourcePath = sourcePath,
                    TargetPath = targetPath,
                    SourceSize = srcInfo.Length,
                    TargetSize = tgtInfo.Length,
                    HexAppended = hexPayload,
                    Version = version,
                    PreviousVersions = newHistoryEntries
                };
            }
            catch (Exception ex)
            {
                return new FileProcessResult
                {
                    Success = false,
                    SourcePath = sourcePath,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 이전 버전 파일들이 현재 경로에 존재하는지 확인하고,
        /// 없으면 다른 위치로 이동되었는지 검색하여 갱신하거나 삭제 상태로 반영합니다.
        /// </summary>
        private static void RefreshEntriesStatus(List<TrackedVersionEntry> entries, string? searchHintDir)
        {
            foreach (var entry in entries)
            {
                if (File.Exists(entry.CurrentPath))
                {
                    entry.Status = (entry.CurrentPath != entry.OriginalPath) ? "Moved" : "Active";
                    continue;
                }

                // 현재 경로에 파일이 없음 -> 이동되었는지 검색
                string? movedPath = LocateMovedFile(entry.FileGuid, entry.FileName, searchHintDir, entry.OriginalPath);
                if (!string.IsNullOrEmpty(movedPath) && File.Exists(movedPath))
                {
                    entry.CurrentPath = movedPath;
                    entry.Status = "Moved";
                }
                else
                {
                    entry.Status = "Deleted";
                }
            }
        }

        /// <summary>
        /// 파일 고유 식별자(GUID) 또는 파일명을 바탕으로 이동된 파일을 스마트하게 검색합니다.
        /// </summary>
        private static string? LocateMovedFile(string guid, string fileName, string? searchHintDir, string originalPath)
        {
            var searchFolders = new List<string>();

            // 1. 힌트 디렉토리 (현재 파일 디렉토리) 및 그 부모
            if (!string.IsNullOrEmpty(searchHintDir) && Directory.Exists(searchHintDir))
            {
                searchFolders.Add(searchHintDir);
                var parent = Directory.GetParent(searchHintDir)?.FullName;
                if (parent != null && Directory.Exists(parent)) searchFolders.Add(parent);
            }

            // 2. 원래 있던 디렉토리의 부모 및 형제 폴더
            string? origDir = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(origDir) && Directory.Exists(origDir))
            {
                searchFolders.Add(origDir);
                var origParent = Directory.GetParent(origDir)?.FullName;
                if (origParent != null && Directory.Exists(origParent) && !searchFolders.Contains(origParent))
                {
                    searchFolders.Add(origParent);
                }
            }

            // 3. 주요 사용자 폴더 (바탕화면, 다운로드, 문서)
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!searchFolders.Contains(desktop) && Directory.Exists(desktop)) searchFolders.Add(desktop);

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!searchFolders.Contains(docs) && Directory.Exists(docs)) searchFolders.Add(docs);

            int scannedFiles = 0;
            const int maxScanLimit = 1500;

            foreach (var folder in searchFolders)
            {
                try
                {
                    // 동일한 파일명을 가진 파일들을 우선 탐색
                    var matches = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories);
                    foreach (var candidate in matches)
                    {
                        scannedFiles++;
                        if (scannedFiles > maxScanLimit) break;

                        // GUID 검증
                        var meta = ReadTrailerMeta(candidate);
                        if (meta != null && !string.IsNullOrEmpty(guid) && meta.FileGuid == guid)
                        {
                            return candidate;
                        }

                        // GUID가 없는 이전 포맷 파일인 경우 파일명이 일치하면 후보로 채택
                        if (meta == null)
                        {
                            return candidate;
                        }
                    }

                    if (scannedFiles > maxScanLimit) break;
                }
                catch
                {
                    // 접근 권한 없는 폴더는 건너뜀
                }
            }

            return null;
        }

        /// <summary>
        /// 파일 끝의 16진수 트레일러로부터 메타데이터(VersionTrailerMeta)를 파싱합니다.
        /// </summary>
        public static VersionTrailerMeta? ReadTrailerMeta(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < LengthHeaderHexDigits) return null;

                int scanLen = (int)Math.Min(fs.Length, 65536);
                fs.Seek(-scanLen, SeekOrigin.End);
                byte[] buffer = new byte[scanLen];
                int read = fs.Read(buffer, 0, scanLen);

                int endIndex = read - 1;
                while (endIndex >= 0 && (buffer[endIndex] == '\r' || buffer[endIndex] == '\n' || buffer[endIndex] == ' ' || buffer[endIndex] == '\0'))
                {
                    endIndex--;
                }

                if (endIndex < LengthHeaderHexDigits - 1) return null;

                string lenHex = Encoding.ASCII.GetString(buffer, endIndex - LengthHeaderHexDigits + 1, LengthHeaderHexDigits);
                if (int.TryParse(lenHex, System.Globalization.NumberStyles.HexNumber, null, out int hexPayloadLen) &&
                    hexPayloadLen > 0 && hexPayloadLen % 2 == 0 && hexPayloadLen <= endIndex - LengthHeaderHexDigits + 1)
                {
                    int start = endIndex - LengthHeaderHexDigits + 1 - hexPayloadLen;
                    string hexString = Encoding.ASCII.GetString(buffer, start, hexPayloadLen);
                    byte[] payloadBytes = Convert.FromHexString(hexString);
                    string jsonOrPath = Encoding.UTF8.GetString(payloadBytes);

                    // JSON 형식 메타데이터인지 확인
                    if (jsonOrPath.TrimStart().StartsWith("{"))
                    {
                        return JsonSerializer.Deserialize<VersionTrailerMeta>(jsonOrPath);
                    }
                    else if (Path.IsPathRooted(jsonOrPath))
                    {
                        // 기존 구버전 포맷 (단순 경로 문자열) 호환
                        return new VersionTrailerMeta
                        {
                            FileGuid = Guid.NewGuid().ToString("N"),
                            ParentPath = jsonOrPath,
                            HistoryEntries = new List<TrackedVersionEntry>
                            {
                                new TrackedVersionEntry
                                {
                                    StepIndex = 1,
                                    OriginalPath = jsonOrPath,
                                    CurrentPath = jsonOrPath,
                                    Status = File.Exists(jsonOrPath) ? "Active" : "Deleted"
                                }
                            }
                        };
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// 메타데이터가 없는 구버전 트레일러 파일들을 위한 Fallback 체인 추적
        /// </summary>
        private static List<TrackedVersionEntry> TracePreviousVersionsFallback(string startFilePath)
        {
            var result = new List<TrackedVersionEntry>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? current = startFilePath;
            visited.Add(current);

            int step = 1;
            while (!string.IsNullOrEmpty(current))
            {
                var meta = ReadTrailerMeta(current);
                string? prevPath = meta?.ParentPath;

                if (string.IsNullOrEmpty(prevPath) || visited.Contains(prevPath))
                    break;

                visited.Add(prevPath);
                result.Add(new TrackedVersionEntry
                {
                    StepIndex = step++,
                    FileGuid = meta?.ParentGuid ?? Guid.NewGuid().ToString("N"),
                    OriginalPath = prevPath,
                    CurrentPath = prevPath,
                    Status = File.Exists(prevPath) ? "Active" : "Deleted"
                });

                current = prevPath;
            }

            return result;
        }
    }
}
