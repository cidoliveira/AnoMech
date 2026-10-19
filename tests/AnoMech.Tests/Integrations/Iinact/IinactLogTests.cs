using AnoMech.Integrations.Iinact;

namespace AnoMech.Tests;

// The member path IinactLog walks, on stand-ins shaped like IINACT's NotACT FormActMain and
// FFXIV_ACT_Plugin's log writer.
public class IinactLogTests
{
    public enum FakeLogMessageType { Territory = 1, StartsCasting = 20 }

    public interface IFakeLogOutput
    {
        void WriteLine(FakeLogMessageType type, DateTime time, string line);
    }

    public sealed class FakeLogOutput : IFakeLogOutput
    {
        public List<(FakeLogMessageType Type, string Body)> Written { get; } = [];

        void IFakeLogOutput.WriteLine(FakeLogMessageType type, DateTime time, string line) => Written.Add((type, line));
    }

    public sealed class FakeDataCollection(FakeLogOutput output)
    {
        private readonly IFakeLogOutput _logOutput = output;
        public IFakeLogOutput Output => _logOutput;
    }

    public sealed class FakeFfxivPlugin(FakeDataCollection data)
    {
        public FakeDataCollection _dataCollection = data;
    }

    public sealed class FakeFormActMain(FakeFfxivPlugin? plugin)
    {
        public FakeFfxivPlugin? FfxivPlugin { get; } = plugin;
        public bool DisableWritingPvpLogFile { get; set; }
    }

    private static (FakeFormActMain Form, FakeLogOutput Output) Iinact()
    {
        var output = new FakeLogOutput();
        return (new FakeFormActMain(new FakeFfxivPlugin(new FakeDataCollection(output))), output);
    }

    [Test]
    public void LinesGoToTheParsersLogWriterAsTheirType()
    {
        var (form, output) = Iinact();
        var log = IinactLog.FromForm(form, out var missing);

        log!.Write(new NetLogEntry(20, "40001234|Kefka"));

        Assert.That(missing, Is.Null);
        Assert.That(output.Written, Is.EqualTo(new[] { (FakeLogMessageType.StartsCasting, "40001234|Kefka") }));
    }

    [Test]
    public void ATypeTheEnumDoesNotNameStillGoesOut()
    {
        var (form, output) = Iinact();

        IinactLog.FromForm(form, out _)!.Write(new NetLogEntry(257, "800375D2"));

        Assert.That((int)output.Written.Single().Type, Is.EqualTo(257));
    }

    [Test]
    public void KeepOffDiskIsIinactsOwnSwitch()
    {
        var (form, _) = Iinact();
        var log = IinactLog.FromForm(form, out _)!;

        log.KeepOffDisk = true;

        Assert.That(form.DisableWritingPvpLogFile, Is.True);
        Assert.That(log.KeepOffDisk, Is.True);
    }

    [Test]
    public void AParserThatIsNotUpYetSaysSo()
    {
        var log = IinactLog.FromForm(new FakeFormActMain(null), out var missing);

        Assert.That(log, Is.Null);
        Assert.That(missing, Is.Not.Null);
    }
}
