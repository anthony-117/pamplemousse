# 0011 — Attractions from Wikidata, ranked by fame; OpenTripMap as fallback
Date: 2026-10-28 · Status: Accepted · Amends the "Places" row of [0003](0003-data-sources-mvp.md) for attractions

## Context
With the OpenTripMap key set, Rome's top attractions were the Temple of Minerva Medica and the Aurelian Walls, not the Colosseum or the Pantheon.
- OpenTripMap's radius search returns the places **nearest** the city point first; 500 results cover only ~2 km in a dense city.
- Its bounding-box search spreads results but returns a sample: Paris's sample has no Eiffel Tower, Louvre or Notre-Dame.
- Its popularity score is coarse (rate 1–3, "h" for heritage), so it cannot tell the Colosseum from a side chapel.
- The same landmark is often several objects (the Colosseum twice), and restaurants in historic buildings are also tagged as attractions.

## Decision
- **Attractions come from Wikidata** (free, no key): places within 10 km of the city with at least 15 Wikipedia articles, ranked by the number of Wikipedia language editions (Colosseum 148, Trevi Fountain 68, Aurelian Walls 48).
- One SPARQL query per city (1–7 s), **cached 7 days**. It returns the 150 most famous items with coordinates; the backend keeps the ones whose Wikidata type names an attraction (museum, basilica, fountain, square, tower, palace, site…), and drops places with a population (districts), demolished places and items without an English name. Filtering by the full type hierarchy on Wikidata's side was exact but took 10–60 s.
- Place ids are `wd:Q…`. Details combine Wikidata (name, type, coordinates, website, photo) with the English Wikipedia summary (description).
- **OpenTripMap stays the fallback**: when Wikidata is down, rate-limited (429) or slower than 12 s, or a small town has too few landmarks, the list is filled from OpenTripMap (bounding-box search, one object per landmark, restaurants excluded). Restaurants stay on OpenTripMap.
- Ticketmaster events with the same name, date, time and venue are shown once (different dates stay).

## Consequences
- Rome: Colosseum, St Peter's, Sistine Chapel, Pantheon, Roman Forum, Trevi. Paris: Eiffel Tower, Louvre, Notre-Dame, Orsay, Arc de Triomphe. Beirut: National Museum, Al-Amin Mosque, Sursock Museum, then OpenTripMap places.
- Attractions work even without `OPENTRIPMAP_API_KEY` (that key is then only needed for restaurants and the fallback).
- The Wikidata Query Service limits heavy use (429 during development). The 7-day cache keeps us well below it; a failure falls back instead of erroring.
- The type-name check is a heuristic: a few non-sights can slip in (e.g. an official residence), and an odd type name can drop a real sight. The word list lives in `WikidataAttractionProvider` and is covered by tests.
- Wikimedia requires a descriptive User-Agent; the backend sends `TravelGenie/0.1 (https://github.com/anthony-117/pamplemousse)`.
