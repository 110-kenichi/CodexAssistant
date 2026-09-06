using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodexAssistant
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid("778fce55-27a0-453f-9295-f7616329a18e")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(CodexOptions), "Codex Assistant", "General", 0, 0, true)]
    [ProvideToolWindow(typeof(ChatWindow), Style = VsDockStyle.Tabbed,
        Window = "{3AE79031-E1BC-11D0-8F78-00A0C9110057}", Orientation = ToolWindowOrientation.Right)]
    public sealed class CodexPackage : AsyncPackage
    {
        internal static CodexPackage Instance { get; private set; }
        internal CodexOptions Options => (CodexOptions)GetDialogPage(typeof(CodexOptions));
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var menu = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menu?.AddCommand(new MenuCommand((s, e) => JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var window = await ShowToolWindowAsync(typeof(ChatWindow), 0, true, DisposalToken);
                    if (window?.Frame == null) throw new InvalidOperationException("Cannot create Codex Assistant window.");
                }
                catch (Exception ex) { ActivityLog.LogError("Codex Assistant", ex.ToString()); }
            }), new CommandID(new Guid("edcd9a21-c8c2-48ba-a3cf-d0a7cae4f204"), 0x0100)));
        }
    }

    public class CodexOptions : DialogPage
    {
        [Browsable(false)]
        public string SelectedModel { get; set; } = "";
        [Browsable(false)]
        public string RecentModels { get; set; } = "";
        [Category("CLI"), DisplayName("Codex executable"), Description("Absolute path to codex.exe, or codex.exe resolved from PATH. npm installations: select the native codex.exe under the package vendor directory. .cmd/.ps1 wrappers are not supported.")]
        public string Executable { get; set; } = "codex.exe";
        [Category("Context"), DisplayName("Include Error List")]
        public bool IncludeErrors { get; set; } = true;
        [Category("Context"), DisplayName("Include Git diff")]
        public bool IncludeGitDiff { get; set; } = true;
        [Category("CLI"), DisplayName("Timeout (minutes)")]
        public int TimeoutMinutes { get; set; } = 10;
    }

    [Guid("0e219798-3c32-430f-b408-536427ba54a5")]
    public sealed class ChatWindow : ToolWindowPane
    {
        public ChatWindow() : base(null) { Caption = "Codex Assistant"; Content = new ChatControl(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) (Content as ChatControl)?.Stop();
            base.Dispose(disposing);
        }
    }
}
