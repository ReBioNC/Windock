using Windock.Core.Island;
using Windock.Core.Settings;
using Windock.Core.Tests.Fakes;

namespace Windock.Core.Tests.Island;

public class IslandPanelsTests
{
    private const string Forecast = """
        {"current":{"time":"2026-10-25T15:00","temperature_2m":29.4,"weather_code":61,"is_day":1},
         "hourly":{"time":["2026-10-25T14:00","2026-10-25T15:00","2026-10-25T16:00","2026-10-25T17:00","2026-10-25T18:00","2026-10-25T19:00","2026-10-25T20:00","2026-10-25T21:00"],
                   "temperature_2m":[30,29.4,28.2,27.9,27,26.1,25.8,25.5],
                   "weather_code":[2,61,61,3,3,0,0,0],
                   "is_day":[1,1,1,1,1,0,0,0]},
         "daily":{"time":["2026-10-25","2026-10-26"],"temperature_2m_max":[32.1,31],"temperature_2m_min":[24.6,24]}}
        """;

    [Fact]
    public void Pages_turn_both_ways_and_wrap()
    {
        var model = new IslandModel(new ManualDelayScheduler(), () => TimeSpan.FromSeconds(2));
        model.PointerEntered(true);

        model.TurnPage(1, 3);
        Assert.Equal(1, model.Page);
        model.TurnPage(-1, 3);
        model.TurnPage(-1, 3);
        Assert.Equal(2, model.Page);
        model.TurnPage(1, 3);
        Assert.Equal(0, model.Page);
    }

    [Fact]
    public void Closing_the_card_goes_back_to_the_first_page_and_a_closed_card_does_not_turn()
    {
        var model = new IslandModel(new ManualDelayScheduler(), () => TimeSpan.FromSeconds(2));
        model.TurnPage(1, 3);
        Assert.Equal(0, model.Page);

        model.PointerEntered(true);
        model.TurnPage(1, 3);
        model.Collapse();
        Assert.Equal(0, model.Page);
    }

    [Fact]
    public void Forecast_is_read_with_the_next_hours()
    {
        var r = OpenMeteo.ParseForecast(Forecast, new DateTime(2026, 10, 25, 15, 3, 0));

        Assert.NotNull(r);
        Assert.Equal(29.4, r.Temperature);
        Assert.Equal(61, r.Code);
        Assert.Equal(32.1, r.High);
        Assert.Equal(24.6, r.Low);
        Assert.Equal(OpenMeteo.HoursShown, r.Hours.Count);
        Assert.Equal(16, r.Hours[0].Time.Hour);
        Assert.False(r.Hours[^1].IsDay);
    }

    [Fact]
    public void Broken_replies_are_null()
    {
        Assert.Null(OpenMeteo.ParseForecast("{}", DateTime.Now));
        Assert.Null(OpenMeteo.ParseForecast("not json", DateTime.Now));
        Assert.Null(OpenMeteo.ParseGeocode("x", "{\"generationtime_ms\":0.5}"));
    }

    [Fact]
    public void City_lookup_names_the_country()
    {
        var p = OpenMeteo.ParseGeocode("jakarta", """{"results":[{"name":"Jakarta","latitude":-6.21462,"longitude":106.84513,"country":"Indonesia"}]}""");

        Assert.Equal(new WeatherPlace("jakarta", "Jakarta, Indonesia", -6.21462, 106.84513), p);
    }

    [Fact]
    public void Forecast_url_uses_dots_and_the_unit()
    {
        var url = OpenMeteo.ForecastUrl(new WeatherPlace("x", "X", -6.21462, 106.84513), TemperatureUnit.Fahrenheit);

        Assert.Contains("latitude=-6.2146&longitude=106.8451", url);
        Assert.Contains("temperature_unit=fahrenheit", url);
        Assert.DoesNotContain("fahrenheit", OpenMeteo.ForecastUrl(new WeatherPlace("x", "X", 1, 2), TemperatureUnit.Celsius));
    }

    [Theory]
    [InlineData(0, true, "Clear", WeatherCodes.Sun)]
    [InlineData(0, false, "Clear", WeatherCodes.Moon)]
    [InlineData(3, true, "Cloudy", WeatherCodes.Cloud)]
    [InlineData(63, true, "Rain", WeatherCodes.Rain)]
    [InlineData(95, true, "Thunderstorm", WeatherCodes.Storm)]
    public void Weather_codes_have_text_and_icon(int code, bool day, string text, string glyph) =>
        Assert.Equal((text, glyph), WeatherCodes.Describe(code, day));

    [Fact]
    public void Device_list_drops_generic_entries_next_to_named_ones()
    {
        var list = DeviceList.Build(new[]
        {
            new DeviceEntry(DeviceKind.Mouse, null),
            new DeviceEntry(DeviceKind.Mouse, "G304"),
            new DeviceEntry(DeviceKind.Mouse, "G304"),
            new DeviceEntry(DeviceKind.Keyboard, null),
            new DeviceEntry(DeviceKind.Keyboard, null),
            new DeviceEntry(DeviceKind.Display, "DELL P2419H"),
        });

        Assert.Equal(new[] { "G304", "Keyboard", "DELL P2419H" }, list.Select(e => e.Name));
    }

    [Fact]
    public void A_bluetooth_device_already_listed_lends_its_battery_and_is_not_repeated()
    {
        var list = DeviceList.Build(new[]
        {
            new DeviceEntry(DeviceKind.Audio, "Headphones (WH-1000XM4)", Note: "Output"),
            new DeviceEntry(DeviceKind.Bluetooth, "WH-1000XM4", 60),
            new DeviceEntry(DeviceKind.Bluetooth, "Galaxy Buds", 40),
        });

        Assert.Equal(2, list.Count);
        Assert.Equal(new DeviceEntry(DeviceKind.Audio, "Headphones (WH-1000XM4)", 60, "Output"), list[0]);
        Assert.Equal(new DeviceEntry(DeviceKind.Bluetooth, "Galaxy Buds", 40), list[1]);
    }

    [Fact]
    public void Gear_that_is_both_keyboard_and_mouse_is_listed_once()
    {
        var list = DeviceList.Build(new[]
        {
            new DeviceEntry(DeviceKind.Mouse, "Gaming Keyboard"),
            new DeviceEntry(DeviceKind.Mouse, "USB Gaming Mouse"),
            new DeviceEntry(DeviceKind.Keyboard, "Gaming Keyboard"),
            new DeviceEntry(DeviceKind.Keyboard, "USB Gaming Mouse"),
            new DeviceEntry(DeviceKind.Keyboard, "MX Receiver"),
            new DeviceEntry(DeviceKind.Mouse, "MX Receiver"),
        });

        Assert.Equal(new[] { "USB Gaming Mouse", "MX Receiver", "Gaming Keyboard" }, list.Select(e => e.Name));
        Assert.Equal(new[] { DeviceKind.Mouse, DeviceKind.Mouse, DeviceKind.Keyboard }, list.Select(e => e.Kind));
    }

    [Fact]
    public void Only_the_current_audio_output_is_listed_without_its_number_or_driver()
    {
        var list = DeviceList.Build(new[]
        {
            new DeviceEntry(DeviceKind.Audio, "1 - Mi Monitor (AMD High Definition Audio Device)", Note: "Output"),
            new DeviceEntry(DeviceKind.Audio, "Speakers (Realtek(R) Audio)"),
            new DeviceEntry(DeviceKind.Audio, "AI Noise-cancelling Output (ASUS Utility)"),
        });

        Assert.Equal("Mi Monitor", Assert.Single(list).Name);
    }

    [Fact]
    public void A_laptop_keyboard_and_touchpad_are_not_listed()
    {
        var list = DeviceList.Build(new[]
        {
            new DeviceEntry(DeviceKind.Mouse, null, BuiltIn: true),
            new DeviceEntry(DeviceKind.Keyboard, "N-KEY Device", BuiltIn: true),
            new DeviceEntry(DeviceKind.Keyboard, null, BuiltIn: true),
            new DeviceEntry(DeviceKind.Display, "Built-in display"),
        });

        Assert.Equal(new[] { "Built-in display" }, list.Select(e => e.Name));
    }

    [Fact]
    public void A_plugged_in_mouse_without_a_product_name_is_still_listed()
    {
        var list = DeviceList.Build(new[] { new DeviceEntry(DeviceKind.Mouse, null, BuiltIn: true), new DeviceEntry(DeviceKind.Mouse, null) });

        Assert.Equal("Mouse", Assert.Single(list).Name);
    }

    [Fact]
    public void Panel_settings_round_trip_with_the_found_city()
    {
        var d = SettingsDefaults.Create();
        var s = d with
        {
            Island = d.Island with
            {
                WeatherPanel = false, City = "Jakarta", Unit = TemperatureUnit.Fahrenheit,
                Place = new WeatherPlace("Jakarta", "Jakarta, Indonesia", -6.2, 106.8),
            },
        };

        Assert.Equal(s, SettingsJson.Parse(SettingsJson.Serialize(s)));
        Assert.Equal("Bandung", SettingsNormalizer.Normalize(d with { Island = d.Island with { City = "  Bandung " } }).Island.City);
    }
}
