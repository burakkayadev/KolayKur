using System.Security.AccessControl;
using System.Security.Principal;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Winget;

public sealed class WinmdBootstrapperTests : IDisposable
{
    private const string WinmdFileName = "Microsoft.Management.Deployment.winmd";

    private static readonly byte[] ResourceBytes = [.. Enumerable.Range(0, 70_000).Select(i => (byte)(i % 251))];

    private readonly string root = Directory.CreateTempSubdirectory("kolaykur-bootstrap-").FullName;

    public void Dispose()
    {
        // Reddetme kuralı bırakılmış klasörlerin silinebilmesi için testler kuralı kendisi geri alır; yine de güvence.
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Present_file_is_left_untouched_and_resource_is_not_opened()
    {
        var target = Path.Combine(root, WinmdFileName);
        File.WriteAllText(target, "mevcut");
        var opened = false;

        var result = WinmdBootstrapper.EnsureExtracted(root, () =>
        {
            opened = true;
            return new MemoryStream(ResourceBytes);
        });

        Assert.Equal(WinmdStatus.AlreadyPresent, result.Status);
        Assert.Null(result.Error);
        Assert.False(opened);
        Assert.Equal("mevcut", File.ReadAllText(target));
    }

    [Fact]
    public void Missing_file_is_extracted_with_the_resource_length_and_content()
    {
        var result = WinmdBootstrapper.EnsureExtracted(root, () => new MemoryStream(ResourceBytes));

        Assert.Equal(WinmdStatus.Extracted, result.Status);
        Assert.Equal(ResourceBytes, File.ReadAllBytes(Path.Combine(root, WinmdFileName)));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void Extraction_works_in_a_folder_with_spaces_and_turkish_letters()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "KolayKur Test Ğ")).FullName;

        var result = WinmdBootstrapper.EnsureExtracted(directory, () => new MemoryStream(ResourceBytes));

        Assert.Equal(WinmdStatus.Extracted, result.Status);
        Assert.Equal(ResourceBytes.Length, new FileInfo(Path.Combine(directory, WinmdFileName)).Length);
    }

    [Fact]
    public void Missing_directory_yields_extraction_failed_and_leaves_nothing_behind()
    {
        var missing = Path.Combine(root, "yok");

        var result = WinmdBootstrapper.EnsureExtracted(missing, () => new MemoryStream(ResourceBytes));

        Assert.Equal(WinmdStatus.ExtractionFailed, result.Status);
        Assert.IsAssignableFrom<IOException>(result.Error);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Read_only_directory_yields_extraction_failed_and_leaves_no_tmp_file()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "salt-okunur")).FullName;
        var info = new DirectoryInfo(directory);
        var user = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(
            user, FileSystemRights.CreateFiles | FileSystemRights.WriteData,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        info.SetAccessControl(CreateSecurityWith(info, deny));
        try
        {
            var result = WinmdBootstrapper.EnsureExtracted(directory, () => new MemoryStream(ResourceBytes));

            Assert.Equal(WinmdStatus.ExtractionFailed, result.Status);
            Assert.IsType<UnauthorizedAccessException>(result.Error);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            var restored = info.GetAccessControl();
            restored.RemoveAccessRule(deny);
            info.SetAccessControl(restored);
        }
    }

    [Fact]
    public void Read_failure_midway_yields_extraction_failed_and_leaves_no_tmp_file()
    {
        var result = WinmdBootstrapper.EnsureExtracted(root, () => new FailingStream(failAfterBytes: 10_000));

        Assert.Equal(WinmdStatus.ExtractionFailed, result.Status);
        Assert.IsAssignableFrom<IOException>(result.Error);
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void Resource_missing_from_the_assembly_is_a_defect_not_a_user_problem()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => WinmdBootstrapper.EnsureExtracted(root, () => null));

        Assert.Contains(WinmdFileName, ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(root));
    }

    private static DirectorySecurity CreateSecurityWith(DirectoryInfo info, FileSystemAccessRule rule)
    {
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        return security;
    }

    /// <summary>Belirli bir bayttan sonra G/Ç hatası veren, aksi halde kaynak baytları okutan akış.</summary>
    private sealed class FailingStream(int failAfterBytes) : Stream
    {
        private readonly MemoryStream inner = new(ResourceBytes);

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (inner.Position >= failAfterBytes)
            {
                throw new IOException("Disk okunamadı.");
            }

            return inner.Read(buffer, offset, Math.Min(count, failAfterBytes - (int)inner.Position));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
