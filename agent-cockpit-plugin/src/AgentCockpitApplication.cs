namespace Loupedeck.AgentCockpitPlugin
{
    using System;

    public class AgentCockpitApplication : ClientApplication
    {
        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
