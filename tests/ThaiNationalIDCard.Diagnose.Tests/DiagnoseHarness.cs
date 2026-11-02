using System;
using System.IO;
using System.Text;
using System.Threading;
using ThaiNationalIDCard.Diagnose;
using ThaiNationalIDCard.Hardware;

namespace ThaiNationalIDCard.Diagnose.Tests
{
    /// <summary>Calls <see cref="WriteSeen"/> on the first write, so a test can tell what had happened by then.</summary>
    internal sealed class WatchedWriter : StringWriter
    {
        public Action? WriteSeen { get; set; }

        public override void Write(char value)
        {
            Fire();
            base.Write(value);
        }

        public override void Write(string? value)
        {
            Fire();
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Fire();
            base.Write(buffer, index, count);
        }

        private void Fire()
        {
            Action? seen = WriteSeen;
            WriteSeen = null;
            seen?.Invoke();
        }
    }

    internal sealed class ToolRun
    {
        public ToolRun(int exitCode, string stdout, string stderr)
        {
            ExitCode = exitCode;
            Stdout = stdout;
            Stderr = stderr;
        }

        public int ExitCode { get; }

        public string Stdout { get; }

        public string Stderr { get; }

        public string All => Stdout + "\n" + Stderr;
    }

    internal static class Tool
    {
        public static ToolRun Run(IRawCardChannel channel, params string[] args) => Run(() => channel, HostPlatform.Linux, args);

        public static ToolRun Run(Func<IRawCardChannel> factory, HostPlatform platform, params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int code = DiagnoseApp.Run(args, stdout, stderr, factory, platform, Xunit.TestContext.Current.CancellationToken);
            return new ToolRun(code, stdout.ToString(), stderr.ToString());
        }
    }

    /// <summary>A card with a sentinel in every byte of every applet, including what no mode may read. All made up.</summary>
    internal static class SentinelCard
    {
        public const string Text = "ZQXSENTINELZQX";

        /// <summary>Made up Thai text, in the Thai name slot, to catch a decoded value in the output.</summary>
        public const string ThaiText = "\u0E17\u0E14\u0E2A\u0E2D\u0E1A\u0E40\u0E17\u0E35\u0E22\u0E21";

        public static ThaiNationalIDCard.Tests.SimulatedCard Build()
        {
            var card = new ThaiNationalIDCard.Tests.SimulatedCard();
            byte[] marker = Encoding.ASCII.GetBytes(Text);
            byte[] image = card.MoiImage;
            for (int i = 0; i < image.Length; i++)
            {
                if (i < ThaiNationalIDCard.Tests.SyntheticImage.CitizenId || i >= ThaiNationalIDCard.Tests.SyntheticImage.CitizenId + 13)
                {
                    image[i] = marker[i % marker.Length];
                }
            }

            byte[] thai = ThaiNationalIDCard.Tests.SyntheticImage.Tis(ThaiText);
            Array.Copy(thai, 0, image, ThaiNationalIDCard.Tests.SyntheticImage.PersonInfo, thai.Length);

            card.AddApplet(ThaiNationalIDCard.Tests.SimulatedCard.NhsoAidHex, Fill(0x200, marker));

            // The value slot has the assumed shape and still holds the sentinel: 7 + 12 characters + 4 NUL.
            byte[] adm = Fill(0x40, marker);
            Array.Copy(Encoding.ASCII.GetBytes("SENTINELZQX0"), 0, adm, 7, 12);
            Array.Clear(adm, 19, 4);
            card.AddApplet(ThaiNationalIDCard.Tests.SimulatedCard.AdmAidHex, adm);
            card.SelectBlob = Fill(10, marker);
            return card;
        }

        public static byte[] Fill(int length, byte[] marker)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = marker[i % marker.Length];
            }

            return data;
        }
    }
}
