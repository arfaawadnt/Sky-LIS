using Platform.Domain.UnitTests.Support;

namespace Platform.Domain.UnitTests.Primitives;

public sealed class EntityTests
{
    [Fact]
    public void A_new_entity_mints_a_version_7_id_in_the_domain()
    {
        var first = new TestEntity();
        var second = new TestEntity();

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.Equal(7, first.Id.Version);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Entities_are_equal_when_type_and_id_match()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(new TestEntity(id), new TestEntity(id));
        Assert.True(new TestEntity(id) == new TestEntity(id));
        Assert.Equal(new TestEntity(id).GetHashCode(), new TestEntity(id).GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal_even_with_the_same_id()
    {
        var id = Guid.CreateVersion7();

        Assert.False(new TestEntity(id).Equals(new OtherEntity(id)));
        Assert.True(new TestEntity(id) != new TestEntity(Guid.CreateVersion7()));
    }
}
