namespace UpdateManager.StaticSiteManagementCoreLibrary;

internal static class LocalStaticSiteLandingPage
{
    internal const string Marker = "<!-- Managed local static site landing page -->";

    internal const string Content = $$"""
        <!doctype html>
        {{Marker}}
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Local apps</title>
          <style>
            body {
              margin: 0; background: #fff; color: #212529;
              font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
            }
            main { padding: 1.5rem 0; }
            h1 { margin: 0 0 1.5rem; font-size: calc(1.375rem + 1.5vw); font-weight: 500; line-height: 1.2; }
            @media (min-width: 1200px) { h1 { font-size: 2.5rem; } }
            .app-grid { display: flex; flex-wrap: wrap; gap: 1rem; align-items: stretch; }
            .app-button {
              box-sizing: border-box; display: flex; align-items: center; justify-content: center;
              max-width: 100%; min-width: 0; padding: .375rem .75rem;
              border: 1px solid #0d6efd; border-radius: .375rem;
              background: #0d6efd; color: #fff; text-align: center; text-decoration: none;
              font-size: 2rem; line-height: 1.2; white-space: normal; overflow-wrap: anywhere;
            }
            .app-button:hover, .app-button:focus-visible {
              border-color: #0a58ca; background: #0b5ed7;
            }
            .app-button:focus-visible { outline: 3px solid #80b5ff; outline-offset: 2px; }
          </style>
        </head>
        <body>
          <main>
            <h1>Local apps</h1>
            <p id="status" role="status">Loading apps…</p>
            <nav id="apps" class="app-grid" aria-label="Available apps"></nav>
          </main>
          <script>
            async function loadApps() {
              const status = document.getElementById("status");
              const list = document.getElementById("apps");

              try {
                const response = await fetch("./apps.txt", { cache: "no-store" });
                if (!response.ok) {
                  throw new Error("The app list is unavailable.");
                }

                const lines = (await response.text()).replace(/^\uFEFF/, "").split(/\r?\n/);
                if (lines.shift() !== "Folder\tDisplayName\tPath") {
                  throw new Error("The app list has an unexpected format.");
                }

                const entries = document.createDocumentFragment();
                for (const line of lines) {
                  if (!line.trim()) {
                    continue;
                  }

                  const [folder, name, path, ...extra] = line.split("\t");
                  if (extra.length || !name || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(folder) ||
                      path !== "/" + folder + "/") {
                    throw new Error("The app list contains an invalid entry.");
                  }

                  const link = document.createElement("a");
                  link.className = "app-button";
                  link.href = path;
                  link.textContent = name;
                  entries.append(link);
                }

                if (entries.childElementCount === 0) {
                  status.textContent = "No apps are currently deployed.";
                  return;
                }

                list.append(entries);
                status.hidden = true;
              } catch {
                status.textContent = "Could not load the app list. Refresh the page to try again.";
              }
            }

            loadApps();
          </script>
        </body>
        </html>
        """;
}
