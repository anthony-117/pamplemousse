# API Keys & Accounts Checklist

You create these accounts yourself. Store keys in the git-ignored `.env` (local) and Google Secret Manager (production) — **never** in OneDrive, the repos or the Flutter app (except platform-restricted Maps keys).

| # | Service | Needed for | Sprint | Where | Notes | Done |
|---|---|---|---|---|---|---|
| 1 | GitHub Student Developer Pack | Credits (Azure, DigitalOcean…) | 0 | education.github.com/pack | USEK email speeds approval | ☐ |
| 2 | GitHub repos `pamplemousse`, `chocomousse` | Backup, CI, backlog | 0 | github.com/new | Private repos | ☐ |
| 3 | Google Cloud project + **budget alert** | Places, Maps, Cloud Run, Secret Manager | 0–1 | console.cloud.google.com | Set a $5 budget alert before enabling APIs | ☐ |
| 4 | ~~Google Places API (New) key~~ | ~~Restaurants~~ | — | — | **On hold (27 Oct 2026):** removed from the code; restaurants come from OpenTripMap `foods`. To be discussed (needs a billing card; Text Search starts at the Pro tier) | — |
| 5 | Google Maps SDK keys (Android, iOS, JS) | Map in chocomousse | 1 | Cloud console | Restrict by app package / domain | ☐ |
| 6 | OpenTripMap key → `OPENTRIPMAP_API_KEY` | Attractions **and restaurants** | 1 | dev.opentripmap.org | Free | ✅ 27 Oct (in pamplemousse `.env`, verified live) |
| 7 | Ticketmaster Discovery key → `TICKETMASTER_API_KEY` | Events | 1 | developer.ticketmaster.com | Free (use the Consumer Key) | ✅ 27 Oct (in pamplemousse `.env`, verified live) |
| 8 | ~~OctoTrip access request~~ (optional since ADR 0010) | Flights, cars | before 2 | xltnapps@gmail.com | Live check returned 403 `ua_blocked` for automated clients. Explain it's a university project and ask for server access | ☐ |
| 9 | Travelpayouts account → `TRAVELPAYOUTS_API_TOKEN` + `TRAVELPAYOUTS_MARKER` | **Flights** (Data API, ADR 0010) + hotels | 2 | travelpayouts.com → Profile → API token; marker = partner ID | Free. Check whether hotel **data** API or only links/widgets | ☐ |
| 10 | Gemini API key (Google AI Studio) | Chatbot, embeddings | 5 | aistudio.google.com | Free tier may train on prompts | ☐ |
| 11 | Neon or Supabase | Cloud Postgres + pgvector | 0 | neon.tech / supabase.com | Free tier | ☐ |
| 12 | Upstash | Cloud Redis | 2 | upstash.com | Free tier | ☐ |
| 13 | Firebase project | Hosting, App Distribution, maybe Auth | 0 | console.firebase.google.com | Same Google Cloud project | ☐ |
| 14 | REST Countries v5 | *Not needed* | — | — | Replaced by static mledoze dataset | — |

No key needed: Open-Meteo, Frankfurter, Nager.Date, Passport Index (CSV), US State Dept advisories, UK FCDO, GeoNames dumps, mledoze/countries.
