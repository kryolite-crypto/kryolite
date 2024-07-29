using Kryolite.Interface;
using Kryolite.Module.Validator;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Kryolite.Validator.Tests;

public class SlotsTests
{
    private readonly Mock<ILogger<Slots>> _loggerMock;
    private readonly Mock<IStoreManager> _storeManagerMock;
    private readonly IServiceProvider _serviceProvider;

    public SlotsTests()
    {        
        _loggerMock = new Mock<ILogger<Slots>>();
        _storeManagerMock = new Mock<IStoreManager>();
        
        var sc = new ServiceCollection();
        sc.AddSingleton(_storeManagerMock.Object);

        _serviceProvider = sc.BuildServiceProvider();
    }

    [Fact]
    public void TryGetNextLeader_ShouldReturnFalse_WhenNoVotesExist()
    {
        var slots = new Slots(_serviceProvider, _loggerMock.Object);

        var lastView = new View { Id = 10 };
        _storeManagerMock.Setup(sm => sm.GetLastView()).Returns(lastView);
        _storeManagerMock.Setup(sm => sm.GetVotesAtHeight(It.IsAny<long>())).Returns(new List<Vote>());

        var result = slots.TryGetNextLeader(out var leader);

        Assert.False(result);
        Assert.Equal(PublicKey.NULL_PUBLIC_KEY, leader.PublicKey);
        Assert.Equal(10, leader.Height);
        Assert.Equal(6, leader.VoteHeight);
        Assert.Equal(0, leader.VoteCount);
    }

    [Fact]
    public void TryGetNextLeader_ShouldReturnFalse_WhenAllVotersAreBanned()
    {
        var slots = new Slots(_serviceProvider, _loggerMock.Object);

        var bannedKey = PublicKey.Random;
        var signature1 = Signature.Random;

        var lastView = new View { Id = 10 };
        var votes = new List<Vote>
        {
            new() { PublicKey = bannedKey, Signature = signature1 }
        };
        
        _storeManagerMock.Setup(sm => sm.GetLastView())
            .Returns(lastView);

        _storeManagerMock.Setup(sm => sm.GetVotesAtHeight(It.IsAny<long>()))
            .Returns(votes);
        
        slots.Ban(bannedKey);

        var result = slots.TryGetNextLeader(out var leader);

        Assert.False(result);
        Assert.Equal(PublicKey.NULL_PUBLIC_KEY, leader.PublicKey);
        Assert.Equal(10, leader.Height);
        Assert.Equal(6, leader.VoteHeight);
        Assert.Equal(1, leader.VoteCount);
    }

    [Fact]
    public void TryGetNextLeader_ShouldReturnTrue_WhenThereAreEligibleVoters()
    {
        var slots = new Slots(_serviceProvider, _loggerMock.Object);
        var key1 = PublicKey.Random;
        var key2 = PublicKey.Random;
        var signature1 = Signature.Random;
        var signature2 = Signature.Random;

        var lastView = new View { Id = 10 };
        var votes = new List<Vote>
        {
            new() { PublicKey = key1, Signature = signature1 },
            new() { PublicKey = key2, Signature = signature2 }
        };

        _storeManagerMock.Setup(sm => sm.GetLastView())
            .Returns(lastView);

        _storeManagerMock.Setup(sm => sm.GetVotesAtHeight(It.IsAny<long>()))
            .Returns(votes);

        var result = slots.TryGetNextLeader(out var leader);

        Assert.True(result);
        Assert.NotEqual(PublicKey.NULL_PUBLIC_KEY, leader.PublicKey);
        Assert.Equal(10, leader.Height);
        Assert.Equal(6, leader.VoteHeight);
        Assert.Equal(2, leader.VoteCount);
    }
}
