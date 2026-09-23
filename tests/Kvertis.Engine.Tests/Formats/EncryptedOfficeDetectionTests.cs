using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tests.Documents;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Formats;

public class EncryptedOfficeDetectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kvertis-tests", Guid.NewGuid().ToString("N"));

    public EncryptedOfficeDetectionTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Encrypted_office_file_is_rejected_as_protected_not_unsupported()
    {
        var path = DocumentTestFiles.CreateFakeEncryptedOffice(Path.Combine(_dir, "secret.docx"));
        var detector = new FormatDetector(new FormatRegistry(), []);

        var ex = await Should.ThrowAsync<ConversionException>(() => detector.DetectAsync(path, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.ProtectedFile);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Cleanup();
    }

    private void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
