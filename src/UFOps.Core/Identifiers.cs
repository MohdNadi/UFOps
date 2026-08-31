namespace UFOps.Core;

public readonly record struct OperationId
{
    public Guid Value { get; }

    private OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Operation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static OperationId New() => new(Guid.CreateVersion7());

    public static OperationId FromGuid(Guid value) => new(value);

    public static OperationId Parse(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException("Invalid operation ID.");
        }

        return value;
    }

    public static bool TryParse(string? text, out OperationId value)
    {
        if (Guid.TryParse(text, out var parsed) && parsed != Guid.Empty)
        {
            value = new OperationId(parsed);
            return true;
        }

        value = default;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}

public readonly record struct EngineId
{
    public string Value { get; }

    public EngineId(string value)
    {
        Value = IdentifierRules.Validate(value, nameof(value));
    }

    public override string ToString() => Value;
}

public readonly record struct CapabilityId
{
    public string Value { get; }

    public CapabilityId(string value)
    {
        Value = IdentifierRules.Validate(value, nameof(value));
    }

    public override string ToString() => Value;
}

public readonly record struct ErrorCode
{
    public string Value { get; }

    public ErrorCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 96 || value[0] is < 'A' or > 'Z' || !value.Contains('.'))
        {
            throw new ArgumentException("Error code must be an uppercase dotted identifier.", nameof(value));
        }

        foreach (var character in value)
        {
            if (!(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.'))
            {
                throw new ArgumentException("Error code contains unsupported characters.", nameof(value));
            }
        }

        Value = value;
    }

    public override string ToString() => Value;
}

internal static class IdentifierRules
{
    internal static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length is < 3 or > 96 || value[0] is < 'a' or > 'z')
        {
            throw new ArgumentException("Identifier must start with a lowercase letter and contain 3-96 characters.", parameterName);
        }

        var previousWasSeparator = false;
        foreach (var character in value)
        {
            var separator = character is '.' or '-';
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9') && !separator)
            {
                throw new ArgumentException("Identifier contains unsupported characters.", parameterName);
            }

            if (separator && previousWasSeparator)
            {
                throw new ArgumentException("Identifier cannot contain adjacent separators.", parameterName);
            }

            previousWasSeparator = separator;
        }

        if (previousWasSeparator)
        {
            throw new ArgumentException("Identifier cannot end with a separator.", parameterName);
        }

        return value;
    }
}
