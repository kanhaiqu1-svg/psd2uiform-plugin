#if UNITY_EDITOR
using UGF.EditorTools.Psd2UGUI.Cli;
namespace UGF.EditorTools.Psd2UGUI
{
    internal static class AiProviderFactory
    {
        internal static CliAiProvider Create(UGUIParser parser)
        {
            return Create(parser != null ? parser.AiProviderConfig.mode : AiMode.Qoder);
        }
        internal static CliAiProvider Create(AiMode mode) => new CliAiProvider(AiProfiles.Preset(mode));
        internal static CliAiProvider CreateById(string id)
        {
            switch (id)
            {
                case "Claude": return Create(AiMode.Claude);
                case "Codex": return Create(AiMode.Codex);
                case "OpenCode": return Create(AiMode.OpenCode);
                case "Qoder": return Create(AiMode.Qoder);
                case "Cursor": return Create(AiMode.Cursor);
                default: return null;
            }
        }
    }
}
#endif
