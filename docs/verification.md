# Initial MVP verification

Verified locally on 22 October 2026, on branch `docsense`.

| Check | Result |
| --- | --- |
| .NET Release build with warnings treated as errors | Passed; zero warnings/errors |
| Unit and PostgreSQL/pgvector integration suite | 36 passed; zero skipped |
| Sample retrieval evaluation | Seven cases passed, including unrelated-question abstention |
| Browser workflow and error recovery | Two Playwright tests passed in Chrome |
| Desktop and phone captures | 1440px and 390px full-page captures; 1536×1024 comp comparison |
| Formatting | Prettier check passed |
| Package restore and audit | Locked restore with NuGetAuditMode=all passed; npm audit reported zero known vulnerabilities |
| Docker | Image built; loopback-hosted app and database started; `/health` returned healthy |
| Independent interface review | `ship`; no material fixes |

The retrieval evaluation caught a ranking tie caused by prematurely capping lexical scores. The final rank uses a monotonic bounded transform, retaining differences between stronger and weaker keyword matches. The fixed suite checks expected text and document attribution.

The integration suite covers actual database search, scopes, deduplication, deletion/cascades, vector model/dimension isolation, reindexing, provider outages, citation rejection, request validation, CSRF-header rejection and unexpected Host rejection. Valid two-page PDF extraction and page references are covered separately.

Ollama HTTP requests are exercised with controlled responses; model weights were not installed and real-model quality was not evaluated. One Docker restore emitted NU1900 because its advisory-data download timed out; the later host restore with all dependency auditing completed successfully. These checks are bounded development evidence, not a security certification.

The chosen white-and-blue composition and its actual tokens are recorded in [DESIGN.md](../DESIGN.md). The portfolio screenshot is [workspace.png](images/workspace.png). Raw test reports and additional screenshots stay in gitignored `artifacts/`.

Hosted CI has not run until the branch is published. No public deployment is included; this version is a trusted local workspace, as described in [README.md](../README.md).
