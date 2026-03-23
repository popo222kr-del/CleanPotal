using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CleanPotal
{
    public class AutoSyncManager
    {
        private DispatcherTimer _timer;
        private Dictionary<string, DateTime> _fileWriteTimes = new Dictionary<string, DateTime>();
        private Action _onSyncAction;

        public AutoSyncManager(Action onSyncAction, params string[] filePaths)
        {
            _onSyncAction = onSyncAction;

            foreach (var path in filePaths)
            {
                _fileWriteTimes[path] = DateTime.MinValue; // 초기값 세팅
            }

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);

            // 🔥 화면이 멈추지 않도록 비동기로 체크 실행
            _timer.Tick += async (s, e) => await CheckFilesAsync();
        }

        public void Start()
        {
            // 프로그램 시작 시 파일 시간 세팅도 뒷단에서 조용히 처리
            Task.Run(() =>
            {
                foreach (var path in _fileWriteTimes.Keys.ToList())
                {
                    _fileWriteTimes[path] = GetLastWriteTimeSafe(path);
                }
            });
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private async Task CheckFilesAsync()
        {
            bool isChanged = false;

            // 🔥 핵심: UI 화면을 방해하지 않고 백그라운드(Task)에서 파일 시간만 체크합니다.
            await Task.Run(() =>
            {
                foreach (var path in _fileWriteTimes.Keys.ToList())
                {
                    DateTime current = GetLastWriteTimeSafe(path);
                    if (current > _fileWriteTimes[path])
                    {
                        _fileWriteTimes[path] = current;
                        isChanged = true;
                    }
                }
            });

            // 변경된 파일이 발견되면 화면(UI)에 반영하라고 신호를 보냅니다.
            if (isChanged)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _onSyncAction?.Invoke();
                });
            }
        }

        private DateTime GetLastWriteTimeSafe(string path)
        {
            try
            {
                if (File.Exists(path)) return File.GetLastWriteTime(path);
            }
            catch { }
            return DateTime.MinValue;
        }
    }
}