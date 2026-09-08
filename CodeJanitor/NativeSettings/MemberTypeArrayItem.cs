using CodeJanitor.Helpers;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace CodeJanitor.NativeSettings
{
#pragma warning disable VSEXTPREVIEW_SETTINGS // Settings APIs are preview.

    /// <summary>
    /// One row of the <see cref="ReorganizingNativeSettings.MemberTypes"/> grid, bridging it to
    /// the classic serialized <see cref="MemberTypeSetting"/> string storage.
    /// </summary>
    internal sealed class MemberTypeArrayItem : IArraySettingItemConvertible
    {
        public string Kind { get; set; } = string.Empty;

        public int Order { get; set; }

        public string EffectiveName { get; set; } = string.Empty;

        public static MemberTypeArrayItem FromMemberTypeSetting(MemberTypeSetting setting)
        {
            return new MemberTypeArrayItem
            {
                Kind = setting.DefaultName,
                Order = setting.Order,
                EffectiveName = setting.EffectiveName,
            };
        }

        public string ToSerializedMemberTypeSetting()
        {
            return (string)new MemberTypeSetting(Kind, EffectiveName, Order);
        }

        public void ReadArraySettingItem(ArraySettingItem item)
        {
            Kind = item.GetValue<string>(ReorganizingNativeSettings.MemberTypeKindPropertyId);
            Order = item.GetValue<int>(ReorganizingNativeSettings.MemberTypeOrderPropertyId);
            EffectiveName = item.GetValue<string>(ReorganizingNativeSettings.MemberTypeNamePropertyId);
        }

        public ArraySettingItem WriteArraySettingItem()
        {
            return new ArraySettingItem
            {
                { ReorganizingNativeSettings.MemberTypeKindPropertyId, Kind },
                { ReorganizingNativeSettings.MemberTypeOrderPropertyId, Order },
                { ReorganizingNativeSettings.MemberTypeNamePropertyId, EffectiveName },
            };
        }
    }

#pragma warning restore VSEXTPREVIEW_SETTINGS
}
