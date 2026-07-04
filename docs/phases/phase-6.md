# Phase 6 — Full docker-compose Ecosystem (EXTRA)

**Status:** COMPLETE · **Completed:** 2026-07-04 · **Commit:** `feat(phase-6): full docker-compose ecosystem`

## Goal

One command boots the whole system — `mongo + rabbitmq + webui + demoservice` — so a first-time evaluator runs the project without a .NET SDK and without per-service `dotnet run`. Merged with **Audit Block 4**: the acceptance test is a literal from-scratch run of the README, not a hand-tuned launch.

## What changed

- **Two multi-stage Dockerfiles** (`src/DynamicConfig.WebUI/Dockerfile`, `src/DynamicConfig.DemoService/Dockerfile`): `sdk:8.0` builds + publishes, `aspnet:8.0` ships the result. Build context is the **repo root** so each app reaches the referenced `DynamicConfig.Library` project; csproj-only restore layer is cached ahead of the source copy. `curl` is added to the runtime image solely so compose can health-check the HTTP surface.
- **`.dockerignore`** at the root keeps host `bin/`/`obj/` (Windows artifacts) and `docs/`, `tests/`, `.git/` out of the Linux build context.
- **compose gains `webui` + `demoservice`**: `depends_on` both infra services with `condition: service_healthy`; env wiring via ASP.NET's `__` nesting (`ConnectionStrings__Mongo`, `ConnectionStrings__RabbitMq`) and the broker opt-in (`DYNAMIC_CONFIG_RABBITMQ_URI`) pointing at the compose service hostnames; ports `8080`/`8081`; `restart: unless-stopped` (which, for the fail-fast demo, IS the ADR 0004 retry mechanism on a boot-time Mongo outage). Demo runs **hybrid mode by design** (broker env var set) with a 30 000 ms poll floor so broker wins are unambiguous.

## From-scratch verdict (Block 4)

`docker compose down -v` + `docker rmi` the two app images, then the literal README command `docker compose up -d --build`:

| Step | Result |
|---|---|
| Build both images from clean, start in dependency order | ✅ infra healthy first, apps started after |
| All four `healthy` | ✅ `docker compose ps` — mongo, rabbitmq, webui, demoservice all healthy |
| Empty DB first run | ✅ `GET /api/configurations` → `[]`; SPA serves the empty-state element ("No configurations yet") |
| Seed via the API the UI posts to | ✅ 3 records created (201) — SERVICE-A ×2, SERVICE-B ×1 |
| Demo consumes its app's records, foreign app isolated | ✅ SERVICE-A sees `SiteName`/`MaxItemCount`; SERVICE-B's `IsBasketEnabled` absent |
| Edit → instant via broker | ✅ **75 ms** pickup vs the 30 000 ms poll floor |

## Bug found and fixed (the from-scratch run earned its keep)

**RabbitMQ readiness race → demo silently polling-only on first boot.** With the original `rabbitmq-diagnostics ping` healthcheck, `service_healthy` went true when the Erlang VM merely responded — *before* the AMQP listener accepted connections. The demo consumer makes exactly **one** boot connection attempt (ADR 0005, no retry machinery — the C11 nuance made concrete): it raced the broker, lost, swallowed the failure by design, and ran polling-only. First-run pickup was 12.7 s (a poll), not the broker; a `docker restart demoservice` (knowledge not in the README) was the only way to engage the broker — so, a real bug.

Fix, at the right layer (compose, not the locked consumer design): healthcheck → **`rabbitmq-diagnostics -q check_port_connectivity`**, which verifies the listeners actually accept connections. `service_healthy` now means "ready for AMQP." Re-run from scratch: consumer attaches on first boot (queue present, no restart), edit propagates in 75 ms. No library/WebUI code touched.

## Machine-specific finding (not a stack bug)

On this host, `GET http://localhost:8080` reaches an **EDB Postgres Enterprise Manager Apache `httpd`** already bound to 8080, not the WebUI container — the same shadow-port hazard documented for native Mongo (27017) and RabbitMQ (5672), now on an app port. The container is correct and healthy; stack correctness was verified over the compose network and via the host's un-shadowed `8081`. The README's shadow-port note is generalized to require 8080/8081 be free; disabling EDB is the operator's call (it is unrelated third-party software), so it was left running.

## Seeding decision

**Empty-DB first run is the intended state; the Web UI is the seed tool.** No seed script, no fixture container — an evaluator adds records through the UI (or the REST API), which also exercises the create path end-to-end. Documented as a one-paragraph note in the README run section with concrete example values.

## Verification

- Full from-scratch drill above, all steps PASS.
- `dotnet test` unaffected (213/213); no application code changed this phase — only Dockerfiles, `.dockerignore`, compose, and docs.

## Evaluator simulation — final pass (2026-07-04, Türkçe rapor)

Rol: case'i değerlendiren şirket yetkilisi; elde yalnızca case PDF'i + README. Temiz başlangıç (`docker compose down -v`), ardından README'nin birebir komutu. İç audit senaryoları değil, yalnızca PDF'in istedikleri test edildi.

| # | PDF şartı | Gözlem | Sonuç |
|---|---|---|---|
| 1 | Tek komutla çalışma | `docker compose up -d --build` → 4/4 container healthy, boş DB ile | ✅ GEÇTİ |
| 2 | Web UI: listeleme / ekleme / güncelleme / isim filtresi | PDF örnek tablosu UI'dan birebir girildi (SiteName, IsBasketEnabled/SERVICE-B, MaxItemCount/IsActive=0); "max" filtresi anında, fetch'siz tek satıra indirdi; Edit ile değer+IsActive güncellendi | ✅ GEÇTİ |
| 3 | Kütüphane kendi aktif kayıtlarını kendi tipinde döner | DemoService (SERVICE-A): `SiteName=soty.io` (string) VAR; `MaxItemCount` IsActive=0 iken YOK; SERVICE-B'nin `IsBasketEnabled` kaydı hiç görünmüyor | ✅ GEÇTİ |
| 4 | `GetValue<string>("SiteName")` → `"soty.io"` | Demo `GET /` ve konsol satırları: `SiteName=soty.io` | ✅ GEÇTİ |
| 5 | Çalışırken eklenen/değişen kayıt güncellenir | Ekleme: UI kaydı 06:20:44 → demo 06:20:44 satırında görünür (aynı saniye, broker; poll tikleri 06:20:35/06:21:05). Güncelleme: 06:23:22 kaydet → 06:23:22 demo satırında `MaxItemCount=75` (restart yok). Polling tabanı compose'da `RefreshTimerIntervalInMs=30000` ile parametrik | ✅ GEÇTİ |
| 6 | Storage erişilemezse son başarılı kayıtlarla devam | Mongo 06:24:19'da durduruldu; 55 sn sonra (≥1 poll döngüsü) demo aynı değerleri servis ediyor, container healthy; Mongo geri açılınca UI listesi tazelenmiş veriyle geldi | ✅ GEÇTİ |
| 7 | Ekstra puanlar | Broker canlı (aynı-saniye yayılım ×2); `dotnet test` tek koşu 213/213 (Library 131 + WebUI 82, 0 fail); docker-compose ✓; README 2 dk okumada projeyi ve çalıştırmayı net anlatıyor | ✅ GEÇTİ |

**Değerlendiren gözüyle takıldığım noktalar (kod defekti değil):**

1. **Mod trace satırı `docker logs`'ta görünmüyor.** README "one startup trace line always states the mode" diyor; kütüphane `System.Diagnostics.Trace` kullandığı ve container'da konsol listener'ı olmadığı için değerlendiren bu satırı `docker logs`'ta bulamıyor. Davranış ms-seviyesi yayılımla kanıtlı ama vaat edilen gözlemlenebilirlik kanalı compose kurulumunda sessiz.
2. **xUnit1031 uyarısı** — `AuditBlock1EdgeTests.cs(121)`: testte bloklayan task operasyonu uyarısı. Koşuyu etkilemiyor (213/213) ama temiz build çıktısında göze çarpıyor.
3. **8080 gölgesi bu makinede IPv4/IPv6'ya bölünmüş durumda:** `127.0.0.1:8080` → EDB Apache (404), `[::1]:8080` → WebUI Kestrel. Tarayıcı IPv6'yı seçtiği için UI çalışıyor; IPv4 istemcileri Apache'ye düşüyor. Ortam bulgusu — README'nin "port boş olsun" uyarısı bu riski zaten açıkça belgeliyor; temiz bir değerlendirme makinesinde yaşanmaz.

Sonuç: PDF'in tüm zorunlu şartları ve ekstra puan kalemleri, yalnızca README bilgisiyle, tek komutluk kurulumdan itibaren doğrulandı. Kod değişikliği gerektiren defect çıkmadı.
