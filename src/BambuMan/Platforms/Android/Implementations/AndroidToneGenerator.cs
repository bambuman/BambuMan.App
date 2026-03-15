using Android.Media;
using BambuMan.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Stream = Android.Media.Stream;

namespace BambuMan.Implementations
{
    public class AndroidToneGenerator : IToneGenerator, IDisposable
    {
        private readonly ILogger<AndroidToneGenerator> logger;
        private readonly Lazy<ToneGenerator?> toneGenerator;
        private readonly Lazy<ToneGenerator?> toneSystemGenerator;
        private readonly Lazy<ToneGenerator?> alarmToneGenerator;

        public AndroidToneGenerator(ILogger<AndroidToneGenerator> logger)
        {
            this.logger = logger;
            toneGenerator = new Lazy<ToneGenerator?>(() => CreateToneGenerator(Stream.Notification));
            toneSystemGenerator = new Lazy<ToneGenerator?>(() => CreateToneGenerator(Stream.System));
            alarmToneGenerator = new Lazy<ToneGenerator?>(() => CreateToneGenerator(Stream.Alarm));
        }

        private ToneGenerator? CreateToneGenerator(Stream stream)
        {
            try
            {
                return new ToneGenerator(stream, 100);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to create ToneGenerator for stream {Stream}", stream);
                return null;
            }
        }

        public async Task PlayTone()
        {
            await Task.Run(() => { toneGenerator.Value?.StartTone(Tone.CdmaPip, 20); });
        }

        public async Task PlayToneSuccess()
        {
            await Task.Run(() => { toneGenerator.Value?.StartTone(Tone.CdmaPip, 60); });
        }

        public async Task PlaySystemTone()
        {
            await Task.Run(() => { toneSystemGenerator.Value?.StartTone(Tone.CdmaSoftErrorLite, 20); });
        }

        public async Task PlayAlarmTone()
        {
            await Task.Run(() => { alarmToneGenerator.Value?.StartTone(Tone.SupError, 120); });
        }

        public void Dispose()
        {
            if (toneGenerator.IsValueCreated) toneGenerator.Value?.Release();
            if (toneSystemGenerator.IsValueCreated) toneSystemGenerator.Value?.Release();
            if (alarmToneGenerator.IsValueCreated) alarmToneGenerator.Value?.Release();
        }
    }
}
