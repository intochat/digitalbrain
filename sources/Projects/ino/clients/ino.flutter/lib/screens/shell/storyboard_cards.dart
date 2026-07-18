import 'shell_card.dart';

/// Dart constants ported verbatim from docs/ino-design/src/data.js CARDS +
/// HOTELS_REPLAN (lines 77–127). Used by DemoRunner to construct ShellCardModel
/// instances when card events fire during storyboard playback.
class StoryboardCards {
  StoryboardCards._();

  static const ShellCardModel flights = ShellCardModel(
    id: 'flights',
    title: 'Flights · Kyiv → Tokyo',
    subtitle: 'mid-budget · 3 candidates',
    cluster: 'travel',
    rows: [
      FlightRow(
        code: 'TK 762',
        route: 'KBP → IST → NRT',
        duration: '15h 25m',
        price: r'$612',
        tag: 'best value',
      ),
      FlightRow(
        code: 'LO 8071',
        route: 'KBP → WAW → HND',
        duration: '14h 50m',
        price: r'$695',
        tag: 'shortest',
      ),
      FlightRow(
        code: 'QR 5113',
        route: 'KBP → DOH → HND',
        duration: '17h 40m',
        price: r'$574',
        tag: 'cheapest',
      ),
    ],
  );

  static const ShellCardModel hotels = ShellCardModel(
    id: 'hotels',
    title: 'Stays · Tokyo, late Oct',
    subtitle: 'rain-friendly · onsen access prioritized',
    cluster: 'travel',
    rows: [
      HotelRow(
        name: 'Hoshinoya Tokyo',
        area: 'Otemachi',
        note: 'urban ryokan · onsen',
        price: r'$240/n',
        tag: 'recall: ryokan +0.62',
      ),
      HotelRow(
        name: 'Andon Ryokan',
        area: 'Asakusa',
        note: 'classic · indoor baths',
        price: r'$95/n',
        tag: 'value',
      ),
      HotelRow(
        name: 'Trunk House',
        area: 'Kagurazaka',
        note: 'private · rainy-day cozy',
        price: r'$310/n',
        tag: 'splurge',
      ),
      HotelRow(
        name: 'Mimaru Akasaka',
        area: 'Akasaka',
        note: 'chain · skipped',
        price: '—',
        tag: 'dimmed',
        dim: true,
      ),
    ],
  );

  static const ShellCardModel itinerary = ShellCardModel(
    id: 'itinerary',
    title: 'Itinerary · 5 days, weather-fit',
    subtitle: 'indoor anchors mapped to rain peaks',
    cluster: 'travel',
    rows: [
      DayRow(day: 'Day 1', weather: '22%', plan: 'Arrive HND · Asakusa walk · Senso-ji at dusk'),
      DayRow(day: 'Day 2', weather: '61%', plan: 'TeamLab Borderless · Shimokitazawa cafés'),
      DayRow(
        day: 'Day 3',
        weather: '78%',
        plan: 'TeamLab Planets (rain anchor) · onsen evening',
        highlight: true,
      ),
      DayRow(day: 'Day 4', weather: '30%', plan: 'Shibuya · Harajuku · Yoyogi park'),
      DayRow(day: 'Day 5', weather: '18%', plan: 'Tsukiji breakfast · depart NRT'),
    ],
  );

  static const ShellCardModel reminder = ShellCardModel(
    id: 'reminder',
    title: 'Reminder · pre-trip',
    subtitle: 'reminders · soft',
    cluster: 'reminders',
    rows: [
      ReminderRow(
        name: 'Check visa requirements',
        when: 'in 3 days',
        tag: 'auto · accept?',
      ),
    ],
  );

  /// Hotels card after the day-3 replan morph.
  static const ShellCardModel hotelsReplan = ShellCardModel(
    id: 'hotels',
    title: 'Stays · Tokyo, late Oct',
    subtitle: 'rain-friendly · onsen access prioritized',
    cluster: 'travel',
    rows: [
      HotelRow(
        name: 'Hoshinoya Tokyo',
        area: 'Otemachi',
        note: 'urban ryokan · onsen',
        price: r'$240/n',
        tag: 'recall: ryokan +0.62',
      ),
      HotelRow(
        name: 'Sakura Ryokan',
        area: 'Iriya',
        note: 'family-run · onsen',
        price: r'$78/n',
        tag: 'swap · day 3 only',
        highlight: true,
      ),
      HotelRow(
        name: 'Andon Ryokan',
        area: 'Asakusa',
        note: 'classic · indoor baths',
        price: r'$95/n',
        tag: 'kept',
      ),
      HotelRow(
        name: 'Trunk House',
        area: 'Kagurazaka',
        note: 'private · rainy-day',
        price: r'$310/n',
        tag: 'dimmed',
        dim: true,
      ),
    ],
  );

  static ShellCardModel? resolve(String id) => switch (id) {
        'flights' => flights,
        'hotels' => hotels,
        'itinerary' => itinerary,
        'reminder' => reminder,
        _ => null,
      };
}
