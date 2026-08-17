using Siri.SharedKernel;

namespace Siri.UnitTests;

public class ResultTests
{
    [Fact]
    public void Success_WithValue_SetsIsSuccessTrueAndExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_WithDomainError_SetsIsFailureTrueAndExposesError()
    {
        var error = DomainError.NotFound("course not found");

        var result = Result.Failure<int>(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Value_OnFailedResult_ThrowsInvalidOperationException()
    {
        var result = Result.Failure<int>(DomainError.Validation("bad input"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccessfulResult()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void NonGenericResult_Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Equal(DomainError.None, result.Error);
    }

    [Fact]
    public void NonGenericResult_Failure_CarriesError()
    {
        var error = DomainError.Forbidden("not allowed");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
