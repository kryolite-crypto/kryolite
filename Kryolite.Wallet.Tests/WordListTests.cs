namespace Kryolite.Wallet.Tests;

public class WordListTests
{
    [Fact]
    public void WordList_ShouldHave2048UniqueWords()
    {
        var wordList = WordList.Words;
        var uniques = new HashSet<string>();

        foreach (var word in wordList)
        {
            uniques.Add(word);
        }

        Assert.Equal(2048, wordList.Length);
        Assert.Equal(2048, uniques.Count);
    }
}
