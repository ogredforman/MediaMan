using MediaMan.Server.LibraryApi;

namespace MediaMan.Server.Tests;

public sealed class SearchRulesTests
{
    [Theory]
    [InlineData("Amélie", "amelie")]
    [InlineData("Spider-Man: No Way Home", "spider man no way home")]
    [InlineData("Schindler's List", "schindlers list")]
    [InlineData("  WALL·E  ", "wall e")]
    [InlineData("Ōkami", "okami")]
    [InlineData("...", "")]
    [InlineData("がっこう", "がっこう")] // dakuten change the letter; they aren't accents
    public void Normalize_folds_case_accents_and_punctuation(string text, string expected)
    {
        SearchRules.Normalize(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("the matrix", "The Matrix", 0)]
    [InlineData("AMELIE", "Amélie", 0)]
    [InlineData("the mat", "The Matrix", 1)]
    [InlineData("matrix", "The Matrix Reloaded", 2)]
    [InlineData("spider man", "Spider-Man", 0)]
    [InlineData("man spider", "Spider-Man", 2)]
    [InlineData("spiderman", "Spider-Man", 3)]
    [InlineData("atri", "The Matrix", 3)]
    public void Rank_orders_exact_then_prefix_then_word_starts_then_contains(string query, string name, int rank)
    {
        SearchRules.Rank(SearchRules.Query.Of(query), name).Should().Be(rank);
    }

    [Theory]
    [InlineData("matrix x", "The Matrix")]
    [InlineData("zzz", "The Matrix")]
    [InlineData("  ", "The Matrix")]
    [InlineData("-", "The Matrix")]
    [InlineData("が", "かもめ食堂")]
    public void Rank_is_null_without_a_match(string query, string name)
    {
        SearchRules.Rank(SearchRules.Query.Of(query), name).Should().BeNull();
    }
}
