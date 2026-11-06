# Domain Model (draft v0.1 — 2026-10-26)

Lives in `Pamplemousse.Domain`. Pure C#, no I/O. Mermaid renders in VS Code, GitHub and Notion.

```mermaid
classDiagram
    direction LR

    class Country {
      string Code  // ISO-3166 alpha-2, e.g. IT
      string Name
      string Capital
      string[] Currencies
      string[] Languages
      string CallingCode
      string FlagUrl
    }
    class Destination {
      Guid Id
      string Name  // city
      string CountryCode
      GeoPoint Location
      string TimeZone
    }
    class Place {
      Guid Id
      string Provider  // GooglePlaces, OpenTripMap
      string ProviderId
      PlaceCategory Category  // Attraction, Restaurant, Hotel, ...
      string Name
      GeoPoint Location
      double? Rating
      int? RatingCount
      int? PriceLevel
      string? PhotoUrl
      string? Description
    }
    class Event {
      Guid Id
      string Provider  // Ticketmaster
      string Name
      string Venue
      DateTimeOffset Start
      Money? PriceFrom
      string Url
    }
    class Offer {
      <<abstract>>
      string Provider
      Money Price
      string DeepLinkUrl
      DateTimeOffset FetchedAt
      DateTimeOffset ExpiresAt
    }
    %% As built, see specs/001-flights/data-model.md
    class FlightOffer {
      string Origin
      string Destination
      DateTimeOffset Departure
      DateTimeOffset? Return
      int Stops
      TimeSpan Duration
      string[] Airlines
    }
    class HotelOffer {
      string HotelName
      Guid? PlaceId
      DateOnly CheckIn
      DateOnly CheckOut
      int? Stars
      Money PricePerNight
    }
    class CarOffer {
      string Vendor
      string CarClass
      DateTimeOffset PickUp
      DateTimeOffset DropOff
    }
    class Money {
      decimal Amount
      string Currency
    }
    class LocationOverview {
      Country Country
      WeatherDay[] Forecast
      ExchangeRate? Rate
      Holiday[] Holidays
      VisaRequirement? Visa
      Advisory? Advisory
    }
    class User {
      Guid Id
      string Email
      string DisplayName
      string PassportCountry
      string HomeCurrency
    }
    class Weights {
      int Budget   // 0..100
      int Culture
      int Food
      int Nature
      int Nightlife
      int Comfort
      int Pace
    }
    class Trip {
      Guid Id
      Guid UserId
      Guid DestinationId
      DateOnly Start
      DateOnly End
      int Travellers
      Money? Budget
      Weights Weights
      TripStatus Status  // Draft, Planned, Booked
    }
    class Itinerary {
      Guid Id
      Guid TripId
      int Version
      Score MatchScore
    }
    class DayPlan {
      int DayNumber
      DateOnly Date
    }
    class ItineraryItem {
      ItemType Type  // Flight, Hotel, Car, Place, Event, Free
      string RefId
      TimeOnly? Start
      TimeOnly? End
      Money? Cost
      Score Score
    }
    class Score {
      double Total  // 0..1, shown as %
      Dictionary~string,double~ Breakdown
      string Explanation
    }

    Offer <|-- FlightOffer
    Offer <|-- HotelOffer
    Offer <|-- CarOffer
    Destination --> Country
    Place --> Destination
    Event --> Destination
    User "1" --> "*" Trip
    Trip --> Destination
    Trip --> Weights
    Trip "1" --> "*" Itinerary
    Itinerary "1" --> "*" DayPlan
    DayPlan "1" --> "*" ItineraryItem
    ItineraryItem --> Score
    Itinerary --> Score
```

## Notes
- **Offers are never stored in Postgres** — they live in Redis with an expiry (~1 h). Only the chosen offer is copied into an `ItineraryItem` when a plan is saved.
- **Scoring:** each criterion is normalised to 0..1 per candidate, multiplied by the user's weight, summed, divided by total weight → `Score.Total`. `Breakdown` keeps each criterion's contribution so the app can explain "96% match".
- `Weights` defaults come from the user's saved preferences; a trip can override them.
- **As built (Sprint 4):** the trip, itinerary, days and items are `TripRequest`, `Plan`, `BuiltPlan`, `PlanDay` and `PlanItem` in `Pamplemousse.Domain/Itinerary`; see [spec 008's data model](../../../specs/008-itinerary/data-model.md).
