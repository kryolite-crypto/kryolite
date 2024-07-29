using Kryolite.Interface;
using Kryolite.Module.Validator;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using Moq;

namespace Kryolite.Validator.Tests;

public class GeneratorTests
{
    private readonly Mock<IStoreManager> _storeManagerMock;
    private readonly Mock<IKeyRepository> _keyRepositoryMock;
    private readonly Generator _generator;

    public GeneratorTests()
    {
        _storeManagerMock = new Mock<IStoreManager>();
        _keyRepositoryMock = new Mock<IKeyRepository>();
        _generator = new Generator(_storeManagerMock.Object, _keyRepositoryMock.Object);
    }

    [Fact]
    public void GenerateView_ShouldCreateNewView_WhenNoPreviousViewExists()
    {
        var publicKey = PublicKey.Random;
        var privateKey = PrivateKey.Random;

        _storeManagerMock.Setup(sm => sm.GetLastView()).Returns((View)null!);
        _keyRepositoryMock.Setup(kr => kr.GetPublicKey()).Returns(publicKey);
        _keyRepositoryMock.Setup(kr => kr.GetPrivateKey()).Returns(privateKey);
        _storeManagerMock.Setup(sm => sm.GetPendingBlocks()).Returns(new List<Block>());
        _storeManagerMock.Setup(sm => sm.GetPendingVotes()).Returns(new List<Vote>());
        _storeManagerMock.Setup(sm => sm.GetPendingTransactions()).Returns(new List<Transaction>());

        _generator.GenerateView();

        _storeManagerMock.Verify(sm => sm.AddView(It.Is<View>(v =>
            v.Id == 1 &&
            v.LastHash == SHA256Hash.NULL_HASH &&
            v.PublicKey == publicKey &&
            v.Blocks.Count == 0 &&
            v.Votes.Count == 0 &&
            v.Transactions.Count == 0
        ), true, true), Times.Once);
    }

    [Fact]
    public void GenerateView_ShouldCreateNewView_WhenPreviousViewExists()
    {
        var publicKey = PublicKey.Random;
        var previousPublicKey = PublicKey.Random;
        var privateKey = PrivateKey.Random;
        var previousHash = SHA256Hash.Random;

        var previousView = new View
        {
            Id = 1,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastHash = previousHash,
            PublicKey = previousPublicKey
        };

        _storeManagerMock.Setup(sm => sm.GetLastView()).Returns(previousView);
        _keyRepositoryMock.Setup(kr => kr.GetPublicKey()).Returns(publicKey);
        _keyRepositoryMock.Setup(kr => kr.GetPrivateKey()).Returns(privateKey);
        _storeManagerMock.Setup(sm => sm.GetPendingBlocks()).Returns(new List<Block>());
        _storeManagerMock.Setup(sm => sm.GetPendingVotes()).Returns(new List<Vote>());
        _storeManagerMock.Setup(sm => sm.GetPendingTransactions()).Returns(new List<Transaction>());

        _generator.GenerateView();

        _storeManagerMock.Verify(sm => sm.AddView(It.Is<View>(v =>
            v.Id == 2 &&
            v.LastHash == previousView.GetHash() &&
            v.PublicKey == publicKey &&
            v.Blocks.Count == 0 &&
            v.Votes.Count == 0 &&
            v.Transactions.Count == 0
        ), true, true), Times.Once);
    }

    [Fact]
    public void GenerateView_ShouldFilterBlocksVotesAndTransactionsBasedOnLastHash()
    {
        var publicKey = PublicKey.Random;
        var previousPublicKey = PublicKey.Random;
        var privateKey = PrivateKey.Random;
        var previousHash = SHA256Hash.Random;
        var nonMatchingHash = SHA256Hash.Random;

        var previousView = new View
        {
            Id = 1,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastHash = previousHash,
            PublicKey = previousPublicKey
        };

        var matchingBlock = new Block { LastHash = previousView.GetHash() };
        var nonMatchingBlock = new Block { LastHash = nonMatchingHash };
        var matchingVote = new Vote { ViewHash = previousView.GetHash() };
        var nonMatchingVote = new Vote { ViewHash = nonMatchingHash };
        var transaction = new Transaction();

        _storeManagerMock.Setup(sm => sm.GetLastView()).Returns(previousView);
        _keyRepositoryMock.Setup(kr => kr.GetPublicKey()).Returns(publicKey);
        _keyRepositoryMock.Setup(kr => kr.GetPrivateKey()).Returns(privateKey);
        _storeManagerMock.Setup(sm => sm.GetPendingBlocks()).Returns(new List<Block> { matchingBlock, nonMatchingBlock });
        _storeManagerMock.Setup(sm => sm.GetPendingVotes()).Returns(new List<Vote> { matchingVote, nonMatchingVote });
        _storeManagerMock.Setup(sm => sm.GetPendingTransactions()).Returns(new List<Transaction> { transaction });

        _generator.GenerateView();

        _storeManagerMock.Verify(sm => sm.AddView(It.Is<View>(v =>
            v.Blocks.SequenceEqual(new List<SHA256Hash> { matchingBlock.GetHash() }) &&
            v.Votes.SequenceEqual(new List<SHA256Hash> { matchingVote.GetHash() }) &&
            v.Transactions.SequenceEqual(new List<SHA256Hash> { transaction.CalculateHash() })
        ), true, true), Times.Once);
    }
}