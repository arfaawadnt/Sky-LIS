using Platform.Domain.Integration;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Integration;

/// <summary>Scenarios G1–G5 and S1 of intake LIS-INT-INT-001 on SiteAgent and AnalyserSampleCode.</summary>
public sealed class SiteAgentTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static SiteAgent Agent(string? config = null) => SiteAgent.Register(" Main lab agent ", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), config, Now);

    [Fact]
    public void G1_Register_happy_path()
    {
        var agent = Agent("{ \"logRetentionDays\": 30 }");

        Assert.Equal(("Main lab agent", 1, true, "{\"logRetentionDays\":30}"), (agent.Name, agent.ConfigVersion, agent.IsActive, agent.ConfigJson));
        Assert.IsType<SiteAgentRegistered>(Assert.Single(agent.DomainEvents));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    [InlineData("\"text\"")]
    public void G1_Configuration_must_be_a_JSON_object_INT_018(string config)
    {
        Assert.Equal(IntegrationCodes.ConfigurationInvalid, Assert.Throws<DomainException>(() => Agent(config)).Code);
    }

    [Fact]
    public void G1_Branch_and_name_are_required()
    {
        Assert.Equal(IntegrationCodes.BranchRequired, Assert.Throws<DomainException>(() => SiteAgent.Register("A", Guid.Empty, Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now)).Code);
        Assert.Equal(IntegrationCodes.AgentNameInvalid, Assert.Throws<DomainException>(() => SiteAgent.Register(" ", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now)).Code);
    }

    [Fact]
    public void G3_Push_and_configuration_changes_increase_the_version()
    {
        var agent = Agent();
        agent.ClearDomainEvents();

        agent.PushConfiguration(Now);
        agent.Configure("{}", Now);                  // unchanged: no new version
        agent.Configure("{\"a\":1}", Now);

        Assert.Equal(3, agent.ConfigVersion);
        Assert.Equal([2, 3], agent.DomainEvents.OfType<SiteAgentConfigPushed>().Select(e => e.ConfigVersion).ToArray());
    }

    [Fact]
    public void G4_G5_Deactivate_then_reactivate_with_a_new_credential()
    {
        var agent = Agent();
        var credential = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();

        agent.Deactivate(Now);
        agent.Deactivate(Now);
        Assert.Equal(IntegrationCodes.AgentInactive, Assert.Throws<DomainException>(() => agent.PushConfiguration(Now)).Code);
        Assert.Equal(IntegrationCodes.AgentInactive, Assert.Throws<DomainException>(agent.EnsureActive).Code);

        agent.Reactivate(credential, user, Now);

        Assert.Equal((true, credential, user), (agent.IsActive, agent.MachineCredentialId, agent.ServiceUserId));
        Assert.Equal(IntegrationCodes.AgentInactive, Assert.Throws<DomainException>(() => agent.Reactivate(credential, user, Now)).Code);
        Assert.Single(agent.DomainEvents.OfType<SiteAgentDeactivated>());
        Assert.Single(agent.DomainEvents.OfType<SiteAgentReactivated>());
    }

    [Fact]
    public void G2_Replace_credential_on_an_active_agent()
    {
        var agent = Agent();
        var credential = Guid.CreateVersion7();

        agent.ReplaceCredential(credential, Guid.CreateVersion7(), Now);

        Assert.Equal(credential, agent.MachineCredentialId);
        Assert.Equal(credential, Assert.Single(agent.DomainEvents.OfType<SiteAgentCredentialReplaced>()).MachineCredentialId);
    }

    [Theory]
    [InlineData(null, false, SiteAgentStatus.Offline)]
    [InlineData(1, false, SiteAgentStatus.Online)]
    [InlineData(1, true, SiteAgentStatus.Error)]
    [InlineData(5, false, SiteAgentStatus.Online)]
    [InlineData(6, false, SiteAgentStatus.Offline)]
    [InlineData(6, true, SiteAgentStatus.Offline)]
    public void Status_is_offline_after_five_minutes_without_a_heartbeat(int? minutesAgo, bool error, SiteAgentStatus expected)
    {
        DateTimeOffset? last = minutesAgo is { } minutes ? Now.AddMinutes(-minutes) : null;
        Assert.Equal(expected, SiteAgent.StatusAt(last, error, Now));
    }

    [Fact]
    public void S1_Sample_codes_need_an_active_analyser_and_a_code()
    {
        var analyser = AnalyserTests.Active();
        var code = AnalyserSampleCode.Create(analyser, Guid.CreateVersion7(), " SER ");
        Assert.Equal(("SER", true), (code.Code, code.IsActive));

        Assert.Equal(IntegrationCodes.SampleCodeInvalid, Assert.Throws<DomainException>(() => code.ChangeCode("")).Code);
        analyser.Deactivate(Now);
        Assert.Equal(IntegrationCodes.AnalyserInactive, Assert.Throws<DomainException>(() => AnalyserSampleCode.Create(analyser, Guid.CreateVersion7(), "X")).Code);
    }
}
