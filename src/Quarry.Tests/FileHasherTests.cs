using Quarry.Generators.Utilities;

namespace Quarry.Tests;

[TestFixture]
public class FileHasherTests
{
    [Test]
    public void ComputeStableHash_SamePath_ReturnsSameHash()
    {
        var hash1 = FileHasher.ComputeStableHash("/src/Models/User.cs");
        var hash2 = FileHasher.ComputeStableHash("/src/Models/User.cs");
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void ComputeStableHash_DifferentPaths_ReturnsDifferentHashes()
    {
        var hash1 = FileHasher.ComputeStableHash("/src/Models/User.cs");
        var hash2 = FileHasher.ComputeStableHash("/src/Models/Order.cs");
        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void ComputeStableHash_NormalizesSlashes()
    {
        var forward = FileHasher.ComputeStableHash("src/Models/User.cs");
        var back = FileHasher.ComputeStableHash("src\\Models\\User.cs");
        Assert.That(forward, Is.EqualTo(back));
    }

    [Test]
    public void ComputeStableHash_NormalizesCase()
    {
        var lower = FileHasher.ComputeStableHash("src/models/user.cs");
        var upper = FileHasher.ComputeStableHash("SRC/MODELS/USER.CS");
        var mixed = FileHasher.ComputeStableHash("Src/Models/User.cs");
        Assert.That(lower, Is.EqualTo(upper));
        Assert.That(lower, Is.EqualTo(mixed));
    }

    [Test]
    public void ComputeStableHash_Returns8HexCharacters()
    {
        var hash = FileHasher.ComputeStableHash("/any/path/file.cs");
        Assert.That(hash.Length, Is.EqualTo(8));
        Assert.That(hash, Does.Match("^[0-9a-f]{8}$"));
    }

    [Test]
    public void ComputeStableHash_MixedSlashesAndCase_AllNormalize()
    {
        var hash1 = FileHasher.ComputeStableHash("C:\\Projects\\App\\Models\\User.cs");
        var hash2 = FileHasher.ComputeStableHash("c:/projects/app/models/user.cs");
        Assert.That(hash1, Is.EqualTo(hash2));
    }
}
