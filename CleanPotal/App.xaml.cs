using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace CleanPotal
{
    public partial class App : Application
    {
        private const string AppUserModelId = "CleanPotal.Desktop";
        private const string SingleInstanceMutexName = "Global\\CleanPotal.SingleInstance";
        private const int SwRestore = 9;

        private Mutex? _singleInstanceMutex;
        private bool _isPrimaryInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 작업표시줄/즐겨찾기 그룹 식별자 고정
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out bool createdNew);
            _isPrimaryInstance = createdNew;

            if (!_isPrimaryInstance)
            {
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);

            CreateDesktopShortcutIfMissing();

            // 1. 계정 DB 초기화 (없으면 users.db 생성 및 admin 계정 자동 추가)
            AuthDatabaseHelper.InitializeDatabase();

            // 2. 메인 화면 대신 로그인 창을 제일 먼저 띄움
            LoginWindow loginWindow = new LoginWindow();
            loginWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_isPrimaryInstance)
            {
                try { _singleInstanceMutex?.ReleaseMutex(); }
                catch (ApplicationException) { }
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _isPrimaryInstance = false;

            base.OnExit(e);
        }

        private static void CreateDesktopShortcutIfMissing()
        {
            const string regKey = @"Software\CleanPotal";
            const string regValue = "DesktopShortcutCreated";
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regKey);
                if (key?.GetValue(regValue) != null) return; // 이미 한 번 생성했으면 다시 안 만듦
            }
            catch { return; }

            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = System.IO.Path.Combine(desktopPath, "CleanPotal.lnk");

                using var writeKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\CleanPotal");
                writeKey?.SetValue("DesktopShortcutCreated", "1");

                // ClickOnce가 이미 바로가기를 만든 경우 중복 생성 안 함
                if (System.IO.File.Exists(shortcutPath)) return;

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return;

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(exePath);
                shortcut.Description = "CleanPotal";
                shortcut.Save();

                using var writeKey2 = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regKey);
                writeKey2?.SetValue(regValue, "1");
            }
            catch { }
        }

        private static void ActivateExistingInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id == current.Id)
                    {
                        continue;
                    }

                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    _ = AllowSetForegroundWindow((uint)process.Id);

                    if (IsIconic(handle))
                    {
                        _ = ShowWindowAsync(handle, SwRestore);
                    }

                    _ = SetForegroundWindow(handle);
                    break;
                }
            }
            catch
            {
                // 인스턴스 전환 실패 시 신규 인스턴스만 종료
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(uint dwProcessId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
    }
}