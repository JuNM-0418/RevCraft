using System;
using System.IO;
using Microsoft.Win32;

namespace RevCraft
{
    public static class ContextMenuManager
    {
        private const string NextVersionKeyPath = @"Software\Classes\*\shell\RevCraft_NextVersion";
        private const string InspectHistoryKeyPath = @"Software\Classes\*\shell\RevCraft_InspectHistory";

        public const string NextVersionMenuText = "다음버전 생성하기";
        public const string InspectHistoryMenuText = "파일이력 확인하기";

        /// <summary>
        /// 윈도우 탐색기 컨텍스트 메뉴에 등록되어 있는지 여부를 확인합니다.
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                using var key1 = Registry.CurrentUser.OpenSubKey(NextVersionKeyPath);
                using var key2 = Registry.CurrentUser.OpenSubKey(InspectHistoryKeyPath);
                return key1 != null && key2 != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 윈도우 탐색기 우클릭 메뉴에 '다음버전 생성하기' 및 '파일이력 확인하기'를 등록합니다.
        /// - 다음버전 생성하기: --silent 인자로 창 없이 백그라운드 파일 생성
        /// - 파일이력 확인하기: revcraft.exe 창이 실행되어 해당 파일의 이전 버전 체인 목록을 화면에 표시
        /// </summary>
        public static (bool Success, string Message) Register()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return (false, "실행 파일 경로를 찾을 수 없습니다.");
                }

                // 1. "다음버전 생성하기" 등록 (무창 백그라운드 모드)
                using (var key = Registry.CurrentUser.CreateSubKey(NextVersionKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("", NextVersionMenuText);
                        key.SetValue("Icon", $"\"{exePath}\"");
                        using var cmdKey = key.CreateSubKey("command");
                        cmdKey?.SetValue("", $"\"{exePath}\" --silent \"%1\"");
                    }
                }

                // 2. "파일이력 확인하기" 등록 (RevCraft 창 실행 모드)
                using (var key = Registry.CurrentUser.CreateSubKey(InspectHistoryKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("", InspectHistoryMenuText);
                        key.SetValue("Icon", $"\"{exePath}\"");
                        using var cmdKey = key.CreateSubKey("command");
                        cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                return (true, "윈도우 탐색기 우클릭 메뉴에 '다음버전 생성하기'와 '파일이력 확인하기'가 등록되었습니다.");
            }
            catch (Exception ex)
            {
                return (false, $"등록 중 오류 발생: {ex.Message}");
            }
        }

        /// <summary>
        /// 윈도우 탐색기 우클릭 메뉴에서 등록을 제거합니다.
        /// </summary>
        public static (bool Success, string Message) Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(NextVersionKeyPath, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(InspectHistoryKeyPath, throwOnMissingSubKey: false);
                return (true, "윈도우 탐색기 우클릭 메뉴에서 모두 제거되었습니다.");
            }
            catch (Exception ex)
            {
                return (false, $"제거 중 오류 발생: {ex.Message}");
            }
        }
    }
}
