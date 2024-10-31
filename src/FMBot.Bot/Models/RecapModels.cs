﻿using Discord.Interactions;

namespace FMBot.Bot.Models;

public enum RecapPeriod
{
    CurrentWeek = 1,
    LastSevenDays = 2,
    CurrentMonth = 3,
    Last30Days = 4,
    PickedMonth = 5,
    CurrentYear = 6,
    Last365Days = 7,
    PickedYear = 8
}


public enum RecapPage
{
    [ChoiceDisplay("Overview")]
    Overview = 1,

    [ChoiceDisplay("Top artists")]
    TopArtists = 2,

    [ChoiceDisplay("Top albums")]
    TopAlbums = 3,

    [ChoiceDisplay("Top tracks")]
    TopTracks = 4,

    [ChoiceDisplay("Top genres")]
    TopGenres = 5,

    [ChoiceDisplay("Top countries")]
    TopCountries = 6,

    // [ChoiceDisplay("Streaks")]
    // Streaks = 7,

    [ChoiceDisplay("Discoveries ⭐")]
    Discoveries = 8,

    [ChoiceDisplay("Bot stats - Overview")]
    BotStats = 10,

    [ChoiceDisplay("Bot stats - Commands")]
    BotStatsCommands = 11,

    [ChoiceDisplay("Bot stats - Shown artists")]
    BotStatsArtists = 12,

    [ChoiceDisplay("Bot stats - Games")]
    Games = 13,
}
