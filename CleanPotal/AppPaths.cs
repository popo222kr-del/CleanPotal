using System;
using System.IO;
using System.ComponentModel;
using System.Windows;

namespace CleanPotal
{
    public static class AppPaths
    {
        public static bool IsDesigner => DesignerProperties.GetIsInDesignMode(new DependencyObject());

        // 🔥 GetDynamicDataRoot()를 통해 개발 환경과 배포 환경의 경로를 완벽히 분리합니다.
        public static readonly string DataRoot = GetDynamicDataRoot();

        public static string ButtonsFilePath => Path.Combine(DataRoot, "buttons.json");
        public static string GetFallbackButtonsPath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "buttons.json");
        public static string VendorsFilePath => Path.Combine(DataRoot, "vendors.json");
        public static string MasterDbConfigPath => Path.Combine(DataRoot, "master_db_config.txt");
        public static string GlobalTemplatesPath => Path.Combine(DataRoot, "global_templates.json");
        public static string WeeklyReportsFilePath => Path.Combine(DataRoot, "weekly_reports.json");
        public static string ProductionMeetingFilePath => Path.Combine(DataRoot, "production_meetings.json");

        public static string ActiveFilePath => Path.Combine(DataRoot, "active_handovers.json");
        public static string DoneFilePath => Path.Combine(DataRoot, "done_handovers.json");
        public static string DoneDeletedExcelPath => Path.Combine(DataRoot, "Deleted_Done_Handovers.xlsx");
        public static string DoneDeletedSheet => "DeletedRecords";

        public static string LegacyHandoverFilePath => Path.Combine(DataRoot, "handover.json");
        public static string LegacyMigratedBakPath => Path.Combine(DataRoot, "handover_migrated.bak");

        // 🚧 JSON→SQLite 이관 스위치. 이 플래그 파일이 DataRoot 에 '있을 때만' 이관을 실행한다.
        //    배포일에 파일 하나만 만들면 그 시점의 JSON을 DB로 덮어쓰기 이관.
        //    (없으면 이관 안 함 → 갭 동안 전 PC 구버전으로 JSON 그대로 사용 가능)
        public static string MigrationFlagPath => Path.Combine(DataRoot, "ENABLE_DB_MIGRATION.flag");
        public static bool DbMigrationEnabled
        {
            get { try { return File.Exists(MigrationFlagPath); } catch { return false; } }
        }

        private static string GetDynamicDataRoot()
        {
            if (IsDesigner) return @"C:\Temp\CleanPotal";

#if DEBUG
            // 🏠 [개발 모드] 집이나 회사에서 Visual Studio로 '시작(F5)'을 눌러 테스트할 때
            // 네트워크 타임아웃을 방지하기 위해 무조건 로컬 C드라이브의 개발용 폴더를 사용합니다.
            string debugPath = @"C:\CleanPotal_Dev_Data";
            if (!Directory.Exists(debugPath))
            {
                try { Directory.CreateDirectory(debugPath); } catch { }
            }
            return debugPath;
#else
            // 🏢 [배포 모드] Visual Studio에서 '게시'하여 공장 PC에 설치/실행될 때
            // C드라이브 임시 폴더로 도망가는 현상(유령 DB)을 막기 위해 무조건 사내 공유 폴더만 바라봅니다.
            return @"\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\DATA";
#endif
        }
    }
}