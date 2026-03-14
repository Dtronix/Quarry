using Quarry.Generators.Utilities;

namespace Quarry.Tests;

[TestFixture]
public class FileHasherTests
{
    [Test]
    public void ComputeFileTag_SamePath_ReturnsSameTag()
    {
        var tag1 = FileHasher.ComputeFileTag("/src/Models/User.cs");
        var tag2 = FileHasher.ComputeFileTag("/src/Models/User.cs");
        Assert.That(tag1, Is.EqualTo(tag2));
    }

    [Test]
    public void ComputeFileTag_DifferentPaths_ReturnsDifferentTags()
    {
        var tag1 = FileHasher.ComputeFileTag("/src/Models/User.cs");
        var tag2 = FileHasher.ComputeFileTag("/src/Models/Order.cs");
        Assert.That(tag1, Is.Not.EqualTo(tag2));
    }

    [Test]
    public void ComputeFileTag_NormalizesSlashes()
    {
        var forward = FileHasher.ComputeFileTag("src/Models/User.cs");
        var back = FileHasher.ComputeFileTag("src\\Models\\User.cs");
        Assert.That(forward, Is.EqualTo(back));
    }

    [Test]
    public void ComputeFileTag_StripsExtension()
    {
        var tag = FileHasher.ComputeFileTag("src/Models/User.cs");
        Assert.That(tag, Is.EqualTo("src_Models_User"));
    }

    [Test]
    public void ComputeFileTag_StripsDriveLetter()
    {
        var tag = FileHasher.ComputeFileTag("C:\\Projects\\App\\Models\\User.cs");
        Assert.That(tag, Is.EqualTo("Projects_App_Models_User"));
    }

    [Test]
    public void ComputeFileTag_StripsLeadingSlash()
    {
        var tag = FileHasher.ComputeFileTag("/src/Models/User.cs");
        Assert.That(tag, Is.EqualTo("src_Models_User"));
    }

    [Test]
    public void ComputeFileTag_PreservesCase()
    {
        var tag = FileHasher.ComputeFileTag("Src/Models/User.cs");
        Assert.That(tag, Is.EqualTo("Src_Models_User"));
    }

    [Test]
    public void ComputeFileTag_MixedSlashes_Normalizes()
    {
        var tag1 = FileHasher.ComputeFileTag("C:\\Projects\\App\\Models\\User.cs");
        var tag2 = FileHasher.ComputeFileTag("C:/Projects/App/Models/User.cs");
        Assert.That(tag1, Is.EqualTo(tag2));
    }

    [Test]
    public void ComputeFileTag_ContainsOnlyValidIdentifierChars()
    {
        var tag = FileHasher.ComputeFileTag("/some path/with spaces/file.name.cs");
        Assert.That(tag, Does.Match("^[A-Za-z0-9_]+$"));
    }
}
