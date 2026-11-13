# UpdateManager.StaticSiteManagementCoreLibrary

Registration prepares a site for development. It installs update/version support,
sets the initial version to the configured .NET major plus `.0`, creates the
source repository, commits and pushes, and saves that commit as the change baseline.
It does not update or push the coordinator's apps.txt, request a build, or wait for NuGet.

Deployment updates dependencies and detects changes against the registration or
last submission baseline. Changed sites receive their next version and wait for
required managed packages before source submission. Unchanged sites are skipped.
Package inspection/publication is limited to sites requiring source submission.
Dependency updates count as source changes, as before.

After source is pushed, deployment synchronizes apps.txt and pushes coordinator
changes before requesting the coordinator build. A new site at `.0` remains staged;
a successfully submitted subsequent version becomes eligible for membership.
Allocated versions with SourceUpdatePending remain staged until submission succeeds.
Existing apps.txt members are preserved even at `.0`, allowing previously published
sites to continue working. Disabled sites are still removed and cache cleanup is retried.
An empty group containing only staged sites does not create apps.txt.

SourceUpdatePending, HostUpdatePending, DeploymentBuildPending and
RenderCacheClearPending retain their existing retry responsibilities. Membership
and dispatch retries do not require new source changes or another version increment.
A successful submission or workflow request does not mean Render finished deploying.

No inventory schema changes or application Program.cs changes are required.
Use this version of the library from both registration and deployment applications.

Validation: source/order review and archive integrity checks were completed.
A .NET 11 build and live GitHub/Render run could not be performed in the editing
workspace because it has no .NET SDK or access to your development environment.
