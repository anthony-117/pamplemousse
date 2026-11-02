using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Api.Errors;

public static class ExceptionStatusMapper
{
    public static int Map(DomainException exception) => exception is NotFoundException
        ? StatusCodes.Status404NotFound
        : StatusCodes.Status400BadRequest;
}
