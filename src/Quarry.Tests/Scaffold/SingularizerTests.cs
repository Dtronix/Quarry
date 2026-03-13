using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class SingularizerTests
{
    [TestCase("users", "user")]
    [TestCase("products", "product")]
    [TestCase("categories", "category")]
    [TestCase("companies", "company")]
    [TestCase("addresses", "address")]
    [TestCase("classes", "class")]
    [TestCase("statuses", "status")]
    [TestCase("dishes", "dish")]
    [TestCase("matches", "match")]
    [TestCase("boxes", "box")]
    [TestCase("wolves", "wolf")]
    [TestCase("lives", "life")]
    [TestCase("knives", "knife")]
    [TestCase("wives", "wife")]
    [TestCase("heroes", "hero")]
    [TestCase("potatoes", "potato")]
    [TestCase("buses", "bus")]
    [TestCase("indexes", "index")]
    [TestCase("olives", "olive")]
    [TestCase("archives", "archive")]
    [TestCase("objectives", "objective")]
    [TestCase("directives", "directive")]
    [TestCase("atives", "ative")]
    public void Singularize_CommonPlurals_ReturnsCorrectSingular(string input, string expected)
    {
        Assert.That(Singularizer.Singularize(input), Is.EqualTo(expected));
    }

    [TestCase("status")]
    [TestCase("sheep")]
    [TestCase("fish")]
    [TestCase("deer")]
    [TestCase("series")]
    public void Singularize_Uncountable_ReturnsSameWord(string input)
    {
        Assert.That(Singularizer.Singularize(input), Is.EqualTo(input));
    }

    [TestCase("people", "person")]
    [TestCase("children", "child")]
    [TestCase("mice", "mouse")]
    [TestCase("women", "woman")]
    public void Singularize_Irregulars_ReturnsCorrectSingular(string input, string expected)
    {
        Assert.That(Singularizer.Singularize(input), Is.EqualTo(expected));
    }

    [Test]
    public void Singularize_EmptyString_ReturnsEmpty()
    {
        Assert.That(Singularizer.Singularize(""), Is.EqualTo(""));
    }

    [Test]
    public void Singularize_SingleCharacter_ReturnsSame()
    {
        Assert.That(Singularizer.Singularize("s"), Is.EqualTo("s"));
    }
}
