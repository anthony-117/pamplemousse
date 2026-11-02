using Pamplemousse.Api.Errors;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Api.Tests.Errors;

public class ExceptionStatusMapperTests
{
    [Fact]
    public void Not_found_maps_to_404() => Assert.Equal(404, ExceptionStatusMapper.Map(new NotFoundException("gone")));

    [Fact]
    public void Broken_rules_map_to_400() => Assert.Equal(400, ExceptionStatusMapper.Map(new DomainException("no", "field")));
}
