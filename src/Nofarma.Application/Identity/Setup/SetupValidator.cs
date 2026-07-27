namespace Nofarma.Application.Identity.Setup;

public static class SetupValidator
{
    public static IReadOnlyDictionary<string, string> Validate(SetupDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        Required(draft.PharmacyName, nameof(draft.PharmacyName), errors);
        Required(draft.TaxIdentifier, nameof(draft.TaxIdentifier), errors);
        Required(draft.Address, nameof(draft.Address), errors);
        Required(draft.DeviceName, nameof(draft.DeviceName), errors);
        Required(draft.AdministratorName, nameof(draft.AdministratorName), errors);
        Required(draft.AdministratorLogin, nameof(draft.AdministratorLogin), errors);

        if (!IsValidTimeZone(draft.TimeZoneId))
        {
            errors[nameof(draft.TimeZoneId)] = "Indique um fuso horário válido.";
        }

        string password = draft.AdministratorPassword;
        bool strong = !string.IsNullOrWhiteSpace(password) &&
            password.Length >= 12 &&
            password.Any(char.IsUpper) &&
            password.Any(char.IsLower) &&
            password.Any(char.IsDigit) &&
            password.Any(character => !char.IsLetterOrDigit(character));
        if (!strong)
        {
            errors[nameof(draft.AdministratorPassword)] =
                "Use pelo menos 12 caracteres com maiúscula, minúscula, número e símbolo.";
        }

        if (!string.Equals(
            draft.AdministratorPassword,
            draft.PasswordConfirmation,
            StringComparison.Ordinal))
        {
            errors[nameof(draft.PasswordConfirmation)] =
                "A confirmação não corresponde à palavra-passe.";
        }

        return errors;
    }

    private static void Required(
        string value,
        string field,
        Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = "Este campo é obrigatório.";
        }
    }

    private static bool IsValidTimeZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
