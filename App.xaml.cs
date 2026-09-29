using System;
using System.IO;
using System.Windows;

namespace RevCraft
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string[] cmdArgs = Environment.GetCommandLineArgs();

            bool isSilent = false;
            string? targetFile = null;

            // cmdArgs[0]은 실행 파일 자체이므로 인덱스 1부터 탐색
            for (int i = 1; i < cmdArgs.Length; i++)
            {
                string arg = cmdArgs[i].Trim('"', ' ');

                if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase))
                {
                    isSilent = true;
                }
                else if (string.IsNullOrEmpty(targetFile) && !arg.StartsWith("-"))
                {
                    targetFile = arg;
                }
            }

            // [1] 탐색기 우클릭 "다음버전 생성하기" (--silent 모드)
            if (isSilent)
            {
                if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
                {
                    try
                    {
                        FileBackupService.ProcessFile(targetFile);
                    }
                    catch
                    {
                        // 백그라운드 모드이므로 예외 발생 시에도 창 없이 조용히 종료
                    }
                }

                // 창을 전혀 띄우지 않고 프로세스 즉시 종료
                Shutdown(0);
                return;
            }

            // [2] 일반 실행 또는 탐색기 우클릭 "파일이력 확인하기" (UI 모드)
            var mainWindow = new MainWindow();
            mainWindow.Show();

            if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
            {
                mainWindow.InspectFile(targetFile);
            }
        }
    }
}
