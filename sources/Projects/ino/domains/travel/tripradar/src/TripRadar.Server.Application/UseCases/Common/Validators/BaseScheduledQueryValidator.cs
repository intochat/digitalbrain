using System.Linq.Expressions;
using System.Text.Json;
using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Common.Validators;

public abstract class BaseScheduledQueryValidator<TCommand> : AbstractValidator<TCommand> where TCommand : class
{
    protected void AddJsonParamRule<TProp>(
        Expression<Func<TCommand, string?>> jsonSelector,
        Func<TProp, bool> predicate,
        string errorMessage,
        string jsonKey)
    {
        RuleFor(jsonSelector)
            .Must(json =>
            {
                if (string.IsNullOrEmpty(json))
                {
                    return true;
                }

                try
                {
                    using var jsonDoc = JsonDocument.Parse(json);
                    if (!jsonDoc.RootElement.TryGetProperty(jsonKey, out var value))
                    {
                        return true;
                    }

                    if (typeof(TProp) == typeof(string))
                    {
                        var strValue = value.GetString();
                        return strValue is not null && predicate((TProp)(object)strValue);
                    }

                    if (typeof(TProp) == typeof(int))
                    {
                        switch (value.ValueKind)
                        {
                            case JsonValueKind.Number:
                            {
                                var intValue = value.GetInt32();
                                return predicate((TProp)(object)intValue);
                            }
                            case JsonValueKind.String when
                                int.TryParse(value.GetString(), out var parsedInt):
                                return predicate((TProp)(object)parsedInt);
                        }
                    }
                    else if (typeof(TProp) == typeof(int?))
                    {
                        switch (value.ValueKind)
                        {
                            case JsonValueKind.Number:
                            {
                                var intValue = value.GetInt32();
                                return predicate((TProp)(object)(int?)intValue);
                            }
                            case JsonValueKind.String when
                                int.TryParse(value.GetString(), out var parsedInt):
                                return predicate((TProp)(object)(int?)parsedInt);
                        }
                    }
                    else if (typeof(TProp) == typeof(decimal?))
                    {
                        if (value.ValueKind == JsonValueKind.Number)
                        {
                            var decimalValue = value.GetDecimal();
                            return predicate((TProp)(object)decimalValue);
                        }

                        if (value.ValueKind == JsonValueKind.String &&
                            decimal.TryParse(value.GetString(), out var parsedDecimal))
                        {
                            return predicate((TProp)(object)parsedDecimal);
                        }
                    }
                    else if (typeof(TProp) == typeof(bool?))
                    {
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                        {
                            var boolValue = value.GetBoolean();
                            return predicate((TProp)(object)boolValue);
                        }

                        if (value.ValueKind == JsonValueKind.String &&
                            bool.TryParse(value.GetString(), out var parsedBool))
                        {
                            return predicate((TProp)(object)parsedBool);
                        }
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            })
            .WithMessage(errorMessage);
    }

    protected static string? ReadJsonStringParameter(string? json, params string[] jsonKeys)
    {
        if (string.IsNullOrWhiteSpace(json) || jsonKeys.Length == 0)
        {
            return null;
        }

        try
        {
            using var jsonDoc = JsonDocument.Parse(json);

            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (!jsonKeys.Any(key => string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
