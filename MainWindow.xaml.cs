using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace RevCraft
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string? _currentOpenedFile;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateShellMenuButton();
        }

        private void UpdateShellMenuButton()
        {
            bool isRegistered = ContextMenuManager.IsRegistered();
            if (isRegistered)
            {
                BtnToggleShellMenu.Content = "✔ 탐색기 메뉴 등록됨 (클릭 시 해제)";
                BtnToggleShellMenu.ToolTip = "윈도우 탐색기 우클릭 메뉴에서 '다음버전 생성하기' 및 '파일이력 확인하기'를 제거합니다.";
            }
            else
            {
                BtnToggleShellMenu.Content = "⚙ 탐색기 우클릭 메뉴 등록";
                BtnToggleShellMenu.ToolTip = "윈도우 탐색기 우클릭 메뉴에 '다음버전 생성하기'와 '파일이력 확인하기'를 등록합니다.";
            }
        }

        private void BtnToggleShellMenu_Click(object sender, RoutedEventArgs e)
        {
            if (ContextMenuManager.IsRegistered())
            {
                var (success, msg) = ContextMenuManager.Unregister();
                MessageBox.Show(msg, "탐색기 메뉴 해제", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            else
            {
                var (success, msg) = ContextMenuManager.Register();
                MessageBox.Show(msg, "탐색기 메뉴 등록", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }

            UpdateShellMenuButton();
        }

        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "조회할 파일 선택",
                Filter = "모든 파일 (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InspectFile(openFileDialog.FileName);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    InspectFile(files[0]);
                }
            }
        }

        /// <summary>
        /// 파일을 선택하거나 탐색기 '파일이력 확인하기'로 열었을 때 호출:
        /// 자동으로 다음 버전을 생성하지 않고 파일 정보 및 이전 버전 체인 목록을 검사/표시합니다.
        /// </summary>
        public void InspectFile(string filePath, string? noticeMessage = null)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("파일이 존재하지 않습니다:\n" + filePath, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var inspectResult = FileBackupService.InspectFile(filePath);
            if (!inspectResult.Success)
            {
                MessageBox.Show($"파일 분석 중 오류가 발생했습니다:\n{inspectResult.ErrorMessage}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _currentOpenedFile = filePath;

            // 파일 정보 패널 표시
            PanelFileInfoCard.Visibility = Visibility.Visible;
            TxtCurrentFileName.Text = inspectResult.FileName;
            TxtCurrentFilePath.Text = inspectResult.FilePath;
            TxtCurrentFileSize.Text = $"{inspectResult.FileSize:N0} bytes";
            TxtCurrentFileDate.Text = inspectResult.LastModified.ToString("yyyy-MM-dd HH:mm:ss");
            TxtActionNotice.Text = noticeMessage ?? string.Empty;

            // 이전 버전 체인 목록 바인딩
            if (inspectResult.PreviousVersions.Count > 0)
            {
                EmptyHistoryBorder.Visibility = Visibility.Collapsed;
                ListHistory.Visibility = Visibility.Visible;
                ListHistory.ItemsSource = inspectResult.PreviousVersions;
                TxtHistoryCount.Text = $" (총 {inspectResult.PreviousVersions.Count}개 이전 버전 체인 추적됨)";
            }
            else
            {
                EmptyHistoryBorder.Visibility = Visibility.Visible;
                ListHistory.Visibility = Visibility.Collapsed;
                ListHistory.ItemsSource = null;
                TxtEmptyNotice.Text = "바이너리 트레일러가 없는 최초 원본 파일입니다. (이전 버전 없음)";
                TxtHistoryCount.Text = " (최초 원본 파일)";
            }

            TxtFooterStatus.Text = $"파일 열기 완료 - {inspectResult.FileName}";
        }

        /// <summary>
        /// 사용자가 명시적으로 '다음 버전 생성하기'를 요청할 때 호출
        /// </summary>
        private void BtnCreateNextVersion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentOpenedFile) || !File.Exists(_currentOpenedFile))
            {
                MessageBox.Show("먼저 대상 파일을 열어주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = FileBackupService.ProcessFile(_currentOpenedFile);
            if (!result.Success)
            {
                MessageBox.Show($"다음 버전 생성 중 오류가 발생했습니다:\n{result.ErrorMessage}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 새로 생성된 파일로 뷰 갱신
            InspectFile(result.TargetPath, $"✔ v{result.Version} 생성 완료! ({Path.GetFileName(result.TargetPath)})");
        }

        private void ListHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListHistory.SelectedItem is TrackedVersionEntry item)
            {
                OpenInExplorer(item.CurrentPath);
            }
        }

        private void ListHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListHistory.SelectedItem is TrackedVersionEntry item)
            {
                OpenInExplorer(item.CurrentPath);
            }
        }

        private void MenuHistoryOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ListHistory.SelectedItem is TrackedVersionEntry item)
            {
                OpenInExplorer(item.CurrentPath);
            }
        }

        private void MenuHistoryCreateNext_Click(object sender, RoutedEventArgs e)
        {
            if (ListHistory.SelectedItem is TrackedVersionEntry item)
            {
                if (item.Exists && File.Exists(item.CurrentPath))
                {
                    var result = FileBackupService.ProcessFile(item.CurrentPath);
                    if (result.Success)
                    {
                        InspectFile(result.TargetPath, $"✔ v{result.Version} 생성 완료! ({Path.GetFileName(result.TargetPath)})");
                    }
                    else
                    {
                        MessageBox.Show($"생성 실패: {result.ErrorMessage}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("해당 파일이 삭제되었거나 현재 위치에 존재하지 않습니다.", "파일 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnOpenCurrentFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentOpenedFile))
            {
                OpenInExplorer(_currentOpenedFile);
            }
        }

        private void OpenInExplorer(string filePath)
        {
            if (!File.Exists(filePath))
            {
                string? folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folder}\"",
                        UseShellExecute = true
                    });
                    MessageBox.Show($"파일은 없지만 폴더를 열었습니다:\n{filePath}", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show($"파일이 삭제되었거나 경로를 찾을 수 없습니다:\n{filePath}", "파일 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 해당 파일이 선택된 상태로 윈도우 탐색기 폴더 열기
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"탐색기 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
