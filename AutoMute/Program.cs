using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Timers;

class Program
{
    private static System.Timers.Timer _unmuteTimer;
    private static readonly object _lock = new object();
    private static bool _isWaitingToUnmute = false;
    private static float _threshold = 0.5f; // 초기 임계값

    static void Main()
    {
        var enumerator = new MMDeviceEnumerator();
        var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        var speaker = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var speakerVolume = speaker.AudioEndpointVolume;

        _unmuteTimer = new System.Timers.Timer(1500);
        _unmuteTimer.Elapsed += (s, e) =>
        {
            lock (_lock)
            {
                if (!_isWaitingToUnmute) return;

                speakerVolume.Mute = false;
                Console.WriteLine("딜레이 후 출력 복구");
                _isWaitingToUnmute = false;
            }
        };
        _unmuteTimer.AutoReset = false;

        Console.WriteLine("시스템 출력 음소거 활성화");
        Console.WriteLine($"현재 임계값: {_threshold}");
        Console.WriteLine("새 임계값을 입력하세요 (예: 0.3):");

        // 임계값을 실시간으로 업데이트하는 스레드 시작
        new Thread(() =>
        {
            while (true)
            {
                var input = Console.ReadLine();
                if (float.TryParse(input, out float newThreshold))
                {
                    lock (_lock)
                    {
                        _threshold = newThreshold;
                        Console.WriteLine($"[업데이트] 임계값이 {_threshold}로 변경되었습니다.");
                    }
                }
                else
                {
                    Console.WriteLine("잘못된 입력입니다. 0.0 ~ 1.0 사이의 숫자를 입력하세요.");
                }
            }
        })
        { IsBackground = true }.Start();

        using (var capture = new WasapiCapture(mic))
        {
            capture.DataAvailable += (s, a) =>
            {
                float sum = 0;
                int samples = a.BytesRecorded / 2;
                for (int i = 0; i < a.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(a.Buffer, i);
                    float sample32 = sample / 32768f;
                    sum += sample32 * sample32;
                }
                float rms = (float)Math.Sqrt(sum / samples);

                lock (_lock)
                {
                    if (rms > _threshold)
                    {
                        _unmuteTimer.Stop();
                        _isWaitingToUnmute = false;

                        if (!speakerVolume.Mute)
                        {
                            speakerVolume.Mute = true;
                            Console.WriteLine("즉시 출력 음소거됨");
                        }
                    }
                    else
                    {
                        if (!_isWaitingToUnmute && speakerVolume.Mute)
                        {
                            _isWaitingToUnmute = true;
                            _unmuteTimer.Start();
                            Console.WriteLine("딜레이 시작...");
                        }
                    }
                }
            };

            capture.StartRecording();
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
