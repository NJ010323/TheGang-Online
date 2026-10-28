using Network;
namespace UnitTests;
public class ChatTests
{
    private static HostedLobby Started()
    {
        var lobby=new HostedLobby("Alice",3);
        lobby.Join("Bob",lobby.InvitationSecret);lobby.Join("Chloé",lobby.InvitationSecret);
        lobby.Start(lobby.Code,lobby.HostIdentity.SessionToken);return lobby;
    }
    [Fact] public void ChatUsesAuthenticatedIdentityAndDoesNotExposeHands()
    {
        var lobby=Started();var host=lobby.HostIdentity;
        var result=lobby.PostChat(lobby.Code,host.SessionToken,new("  Prêts pour le vote ?  "));
        var message=Assert.Single(result.ChatMessages);
        Assert.Equal(host.PlayerId,message.PlayerId);Assert.Equal("Alice",message.PlayerName);
        Assert.Equal("Prêts pour le vote ?",message.Text);
        Assert.All(result.Players.Where(p=>p.Id!=host.PlayerId),p=>Assert.Empty(p.Hand));
        Assert.Throws<UnauthorizedAccessException>(()=>lobby.PostChat(lobby.Code,"invalid",new("Bonjour")));
        Assert.Throws<InvalidOperationException>(()=>lobby.PostChat(lobby.Code,host.SessionToken,new("Encore")));
    }
    [Theory][InlineData("")][InlineData("   ")][InlineData("bonjour\nseconde ligne")][InlineData("bonjour\0")]
    public void RejectsEmptyAndControlCharacters(string text)
    {
        var lobby=Started();Assert.Throws<ArgumentException>(()=>lobby.PostChat(lobby.Code,lobby.HostIdentity.SessionToken,new(text)));
    }
    [Fact] public void RejectsOversizedMessages()
    {
        var lobby=Started();Assert.Throws<ArgumentException>(()=>lobby.PostChat(lobby.Code,lobby.HostIdentity.SessionToken,new(new string('x',281))));
    }
    [Fact] public async Task TwoHttpClientsExchangeChatWhileKeepingPrivateHands()
    {
        await using var server=await LobbyHttpServer.StartAsync("Alice",3);
        using var host=new LobbyHttpClient(server.LocalAddress);host.SetHostIdentity(server.Lobby.HostIdentity);
        using var bob=new LobbyHttpClient(server.LocalAddress);await bob.JoinAsync("Bob",server.Lobby.InvitationSecret);
        using var chloe=new LobbyHttpClient(server.LocalAddress);await chloe.JoinAsync("Chloé",server.Lobby.InvitationSecret);
        await host.StartAsync();await bob.SendChatAsync("On vote pour Chloé ?");
        var received=await chloe.GetGameAsync();Assert.Equal("Bob",Assert.Single(received.ChatMessages).PlayerName);
        Assert.All(received.Players.Where(p=>p.Id!=chloe.Identity!.PlayerId),p=>Assert.Empty(p.Hand));
    }
}
