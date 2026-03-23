using System;
using System.IO;
using System.ComponentModel;
using System.Windows;

namespace CleanPotal
{
    public static class AppPaths
    {
        public static bool IsDesigner => DesignerProperties.GetIsInDesignMode(new DependencyObject());
        public static readonly string DataRoot = GetDynamicDataRoot();

        public static string ButtonsFilePath => Path.Combine(DataRoot, "buttons.json");
        public static string GetFallbackButtonsPath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "buttons.json");

        public static string ActiveFilePath => Path.Combine(DataRoot, "active_handovers.json");
        public static string DoneFilePath => Path.Combine(DataRoot, "done_handovers.json");
        public static string DoneDeletedExcelPath => Path.Combine(DataRoot, "Deleted_Done_Handovers.xlsx");
        public static string DoneDeletedSheet => "DeletedRecords";

        public static string LegacyHandoverFilePath => Path.Combine(DataRoot, "handover.json");
        public static string LegacyMigratedBakPath => Path.Combine(DataRoot, "handover_migrated.bak");

        private static string GetDynamicDataRoot()
        {
            if (IsDesigner) return @"C:\Temp\CleanPotal";

            string companyPath = @"\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\DATA";
            string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CleanPotal");
            string targetDir = localDir;

            try
            {
                if (Directory.Exists(@"\\10.10.40.98\천안공장"))
                {
                    if (!Directory.Exists(companyPath)) Directory.CreateDirectory(companyPath);
                    targetDir = companyPath;
                }
            }
            catch { }

            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            // 🔥 훼손된 버튼 데이터 100% 완벽 복구 로직
            RestoreOriginalButtonsJson(Path.Combine(targetDir, "buttons.json"));
            EnsureDefaultJson(Path.Combine(targetDir, "active_handovers.json"));
            EnsureDefaultJson(Path.Combine(targetDir, "done_handovers.json"));

            return targetDir;
        }

        // 🔥 강제 복구 로직 강화
        private static void RestoreOriginalButtonsJson(string path)
        {
            bool shouldRestore = false;

            if (!File.Exists(path))
            {
                shouldRestore = true;
            }
            else
            {
                try
                {
                    string content = File.ReadAllText(path);
                    // 🔥 파일 안에 버튼 데이터인 "금강쿼츠"가 아예 없다면? 
                    // 예전 버그로 알맹이가 다 날아간 상태이므로 강제 복구 발동!
                    if (!content.Contains("금강쿼츠"))
                    {
                        shouldRestore = true;
                    }
                }
                catch { }
            }

            if (shouldRestore)
            {
                string originalJson = @"[
  {
    ""group"": ""A급 QTZ"",
    ""items"": [
      { ""title"": ""금강쿼츠 (A급)"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정\\09. 금강\\A급 금강 세정코드 LIST.xlsx"", ""type"": ""file"" },
      { ""title"": ""금강쿼츠 (B급)"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정\\09. 금강\\금강쿼츠 B급 전산 등록 SHEET.xlsx"", ""type"": ""file"" },
      { ""title"": ""영신쿼츠(A급)"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정\\13. 영신\\영신 A급 세정 단가 리스트.xlsm"", ""type"": ""file"" },
      { ""title"": ""영신쿼츠(B급)"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정\\13. 영신\\영신 B급 전산 등록 SHEET.xlsx"", ""type"": ""file"" },
      { ""title"": ""영신쿼츠(수출)"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정\\13. 영신\\영신쿼츠(수출, 유진).xlsx"", ""type"": ""file"" }
    ]
  },
  {
    ""group"": ""세메스"",
    ""items"": [
      { ""title"": ""우암"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\003. WOOAM 2026 입·출고 관리 SHEET.xlsx"", ""type"": ""file"" },
      { ""title"": ""세메스 현황 입·출고 관리"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\001. 세메스_2026년.xlsx"", ""type"": ""file"" },
      { ""title"": ""SEMES 분석 DATA"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\002. SEMES 제품 분석 데이터 & SPC,MUS 청소 관리.xlsx"", ""type"": ""file"" }
    ]
  },
  {
    ""group"": ""기타"",
    ""items"": [
      { ""title"": ""눈관리, 폐기품 LIST"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\000. 눈관리 요청, 폐기품 LIST.xlsm"", ""type"": ""file"" },
      { ""title"": ""MES 등록 요청"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\004. MES 등록용 제품 세정 코드.xlsx"", ""type"": ""file"" },
      { ""title"": ""삼성 및 기타 담당자 연락처"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀_Office\\004. 업체 담당자 LIST\\기타업체 및 삼성 라인 담당자.xlsx"", ""type"": ""file"" }
    ]
  },
  {
    ""group"": ""폴더"",
    ""items"": [
      { ""title"": ""기타세정 폴더"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정"", ""type"": ""folder"" },
      { ""title"": ""외주세정 폴더"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀\\부서 공유 폴더\\03. 기타세정\\30. 외주세정"", ""type"": ""folder"" },
      { ""title"": ""25. 생산 Inform 자료"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료"", ""type"": ""folder"" },
      { ""title"": ""표준 및 오디트자료"", ""path"": ""\\\\10.10.40.98\\천안공장\\40. 세정 컨설팅"", ""type"": ""folder"" }
    ]
  },
  {
    ""group"": ""DOME"",
    ""items"": [
      { ""title"": ""QTZ DOME 현황"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀_Office\\000. QTZ DOME 입, 출고 현황 LIST.xlsx"", ""type"": ""file"" },
      { ""title"": ""QTZ DOME 외주 입고 분석"", ""path"": ""\\\\10.10.40.98\\Diff 세정팀_Office\\000. QTZ DOME ICP-MS 분석.xlsx"", ""type"": ""file"" }
    ]
  },
  {
    ""group"": ""Daily"",
    ""items"": [
      { ""title"": ""인수인계"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\001. 인수인계\\2. 인수인계서.xlsx"", ""type"": ""file"" },
      { ""title"": ""배차 LIST"", ""path"": ""\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\006. Daily 배차 LIST.xlsx"", ""type"": ""file"" }
    ]
  }
]";
                try { File.WriteAllText(path, originalJson, System.Text.Encoding.UTF8); } catch { }
            }
        }

        private static void EnsureDefaultJson(string path)
        {
            if (!File.Exists(path))
            {
                try { File.WriteAllText(path, "[]", System.Text.Encoding.UTF8); } catch { }
            }
        }
    }
}