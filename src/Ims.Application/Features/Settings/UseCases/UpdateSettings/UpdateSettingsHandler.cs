using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class UpdateSettingsHandler(IRepository<SystemSetting> settings, ISystemSettings current, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<UpdateSettingsCommand, SettingsDto>
{
    public async Task<SettingsDto> HandleAsync(UpdateSettingsCommand cmd, CancellationToken ct)
    {
        var before = await current.GetAsync(ct);
        var rows = (await settings.ListAsync(ct)).ToDictionary(s => s.Key);
        void Set(string key, string value)
        {
            if (rows.TryGetValue(key, out var row)) row.ChangeValue(value);
            else settings.Add(SystemSetting.Create(key, value));
        }
        Set(SettingKeys.AllowNegativeStock, SettingValues.From(cmd.Data.AllowNegativeStock));
        Set(SettingKeys.AllowSelfApproval, SettingValues.From(cmd.Data.AllowSelfApproval));
        Set(SettingKeys.LowStockAlertCooldownHours, cmd.Data.LowStockAlertCooldownHours.ToString());
        audit.Record(AuditActions.SettingsUpdated, AuditEntities.SystemSetting, AuditEntities.All, before, cmd.Data);
        await uow.SaveChangesAsync(ct);
        current.Invalidate();
        return cmd.Data;
    }
}
