using System.Windows;

namespace CleanPotal
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. 계정 DB 초기화 (없으면 users.db 생성 및 admin 계정 자동 추가)
            AuthDatabaseHelper.InitializeDatabase();

            // 2. 메인 화면 대신 로그인 창을 제일 먼저 띄움
            LoginWindow loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}