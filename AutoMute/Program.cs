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

    static void Main()
    {
        float threshold = 0.95f; // 0.0~1.0 (실험적으로 조정)
        var enumerator = new MMDeviceEnumerator();

        var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        var speaker = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var speakerVolume = speaker.AudioEndpointVolume;

        // ms단위 딜레이 타이머 설정
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

        using (var capture = new WasapiCapture(mic))
        {
            capture.DataAvailable += (s, a) =>
            {
                float max = 0;
                for (int i = 0; i < a.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(a.Buffer, i);
                    float sample32 = sample / 32768f;
                    max = Math.Max(max, Math.Abs(sample32));
                }

                lock (_lock)
                {
                    if (max > threshold)
                    {
                        // 입력이 임계값 이상: 즉시 음소거 & 타이머 중지
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
                        // 입력이 임계값 미만: 딜레이 후 해제
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
