using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Swan;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class PlayBuilder : BaseBuilder
{
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly GenreService _genreService;
    private readonly SettingService _settingService;
    private readonly TimeService _timeService;
    private readonly TrackService _trackService;
    private readonly UserService _userService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private InteractiveService Interactivity { get; }

    public PlayBuilder(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        UserService userService,
        WhoKnowsPlayService whoKnowsPlayService,
        CensorService censorService,
        WhoKnowsArtistService whoKnowsArtistService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        WhoKnowsTrackService whoKnowsTrackService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        TimeService timeService,
        GenreService genreService,
        TrackService trackService)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._censorService = censorService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this.Interactivity = interactivity;
        this._timeService = timeService;
        this._genreService = genreService;
        this._trackService = trackService;
    }

    public async Task<ResponseModel> NowPlayingAsync(
        string prfx,
        IGuild discordGuild,
        IChannel discordChannel,
        IUser discordUser,
        User contextUser,
        UserSettingsModel userSettings)
    {
        this._embedAuthor = new EmbedAuthorBuilder();
        this._embed = new EmbedBuilder()
            .WithColor(DiscordConstants.LastFmColorRed);
        this._embedFooter = new EmbedFooterBuilder();

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(contextUser.SessionKeyLastFm))
        {
            sessionKey = contextUser.SessionKeyLastFm;
        }

        Response<RecentTrackList> recentTracks;

        if (!userSettings.DifferentUser)
        {
            if (contextUser.LastIndexed == null)
            {
                _ = this._indexService.IndexUser(contextUser);
                recentTracks = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm,
                    useCache: true, sessionKey: sessionKey);
            }
            else
            {
                recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(contextUser);
            }
        }
        else
        {
            recentTracks =
                await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm, useCache: true);
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            var errorEmbed =
                GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.Embed = errorEmbed.Build();
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var embedType = contextUser.FmEmbedType;

        if (discordGuild != null)
        {
            var guild = await this._guildService.GetGuildAsync(discordGuild.Id);
            if (guild?.FmEmbedType != null)
            {
                embedType = guild.FmEmbedType.Value;
            }

            if (guild != null)
            {
                await this._indexService.UpdateGuildUser(await discordGuild.GetUserAsync(contextUser.DiscordUserId),
                    contextUser.UserId, guild);
            }
        }

        var totalPlaycount = recentTracks.Content.TotalAmount;

        var currentTrack = recentTracks.Content.RecentTracks[0];
        var previousTrack = recentTracks.Content.RecentTracks.Count > 1 ? recentTracks.Content.RecentTracks[1] : null;
        if (userSettings.DifferentUser)
        {
            totalPlaycount = recentTracks.Content.TotalAmount;
        }

        if (!userSettings.DifferentUser)
        {
            this._whoKnowsPlayService.AddRecentPlayToCache(contextUser.UserId, currentTrack);
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(discordGuild, discordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        var fmText = "";
        var footerText = "";

        if (!userSettings.DifferentUser &&
            !currentTrack.NowPlaying &&
            currentTrack.TimePlayed.HasValue &&
            currentTrack.TimePlayed < DateTime.UtcNow.AddHours(-1) &&
            currentTrack.TimePlayed > DateTime.UtcNow.AddDays(-5))
        {
            footerText +=
                $"Using Spotify and fm lagging behind? Check '{prfx}outofsync'\n";
        }

        if (currentTrack.Loved)
        {
            footerText +=
                $"❤️ Loved track | ";
        }

        if (embedType is FmEmbedType.TextMini or FmEmbedType.TextFull or FmEmbedType.EmbedTiny)
        {
            if (!userSettings.DifferentUser)
            {
                footerText +=
                    $"{requesterUserTitle} has ";
            }
            else
            {
                footerText +=
                    $"{userSettings.UserNameLastFm} (requested by {requesterUserTitle}) has ";
            }
        }
        else
        {
            footerText +=
                $"{userSettings.UserNameLastFm} has ";
        }


        if (!userSettings.DifferentUser)
        {
            switch (contextUser.FmCountType)
            {
                case FmCountType.Track:
                    var trackPlaycount =
                        await this._whoKnowsTrackService.GetTrackPlayCountForUser(currentTrack.ArtistName,
                            currentTrack.TrackName, contextUser.UserId);
                    if (trackPlaycount.HasValue)
                    {
                        footerText += $"{trackPlaycount} scrobbles on this track | ";
                    }

                    break;
                case FmCountType.Album:
                    if (!string.IsNullOrEmpty(currentTrack.AlbumName))
                    {
                        var albumPlaycount =
                            await this._whoKnowsAlbumService.GetAlbumPlayCountForUser(currentTrack.ArtistName,
                                currentTrack.AlbumName, contextUser.UserId);
                        if (albumPlaycount.HasValue)
                        {
                            footerText += $"{albumPlaycount} scrobbles on this album | ";
                        }
                    }

                    break;
                case FmCountType.Artist:
                    var artistPlaycount =
                        await this._whoKnowsArtistService.GetArtistPlayCountForUser(currentTrack.ArtistName,
                            contextUser.UserId);
                    if (artistPlaycount.HasValue)
                    {
                        footerText += $"{artistPlaycount} scrobbles on this artist | ";
                    }

                    break;
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        footerText += $"{totalPlaycount} total scrobbles";

        switch (embedType)
        {
            case FmEmbedType.TextMini:
            case FmEmbedType.TextFull:
                if (embedType == FmEmbedType.TextMini)
                {
                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();
                }
                else if (previousTrack != null)
                {
                    fmText += $"**Current track**:\n";

                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();

                    fmText += $"\n" +
                              $"**Previous track**:\n";

                    fmText += StringService.TrackToString(previousTrack).FilterOutMentions();
                }

                fmText +=
                    $"`{footerText.FilterOutMentions()}`";

                response.ResponseType = ResponseType.Text;
                response.Text = fmText;
                break;
            default:
                if (embedType == FmEmbedType.EmbedMini || embedType == FmEmbedType.EmbedTiny)
                {
                    fmText += StringService.TrackToLinkedString(currentTrack, contextUser.RymEnabled);
                    this._embed.WithDescription(fmText);
                }
                else if (previousTrack != null)
                {
                    this._embed.AddField("Current:",
                        StringService.TrackToLinkedString(currentTrack, contextUser.RymEnabled));
                    this._embed.AddField("Previous:",
                        StringService.TrackToLinkedString(previousTrack, contextUser.RymEnabled));
                }

                string headerText;
                if (currentTrack.NowPlaying)
                {
                    headerText = "Now playing - ";
                }
                else
                {
                    headerText = embedType == FmEmbedType.EmbedMini
                        ? "Last track for "
                        : "Last tracks for ";
                }

                headerText += embedTitle;

                if (!currentTrack.NowPlaying && currentTrack.TimePlayed.HasValue)
                {
                    footerText += " | Last scrobble:";
                    this._embed.WithTimestamp(currentTrack.TimePlayed.Value);
                }

                this._embedAuthor.WithName(headerText);
                this._embedAuthor.WithUrl(recentTracks.Content.UserUrl);

                if (discordGuild != null && !userSettings.DifferentUser)
                {
                    var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingTrack(contextUser.UserId,
                        discordGuild.Id, currentTrack.ArtistName, currentTrack.TrackName);

                    if (guildAlsoPlaying != null)
                    {
                        footerText += "\n";
                        footerText += guildAlsoPlaying;
                    }
                }

                if (!string.IsNullOrWhiteSpace(footerText))
                {
                    this._embedFooter.WithText(footerText);
                    this._embed.WithFooter(this._embedFooter);
                }

                if (embedType != FmEmbedType.EmbedTiny)
                {
                    this._embedAuthor.WithIconUrl(discordUser.GetAvatarUrl());
                    this._embed.WithAuthor(this._embedAuthor);
                    this._embed.WithUrl(recentTracks.Content.UserUrl);
                }

                if (currentTrack.AlbumCoverUrl != null && embedType != FmEmbedType.EmbedTiny)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(discordGuild, discordChannel,
                        currentTrack.AlbumName, currentTrack.ArtistName, currentTrack.AlbumCoverUrl);
                    if (safeForChannel.Result)
                    {
                        this._embed.WithThumbnailUrl(currentTrack.AlbumCoverUrl);
                    }
                }

                response.Embed = this._embed.Build();
                break;
        }

        return response;
    }

    public async Task<ResponseModel> RecentAsync(
        IGuild discordGuild,
        IUser discordUser,
        User contextUser,
        UserSettingsModel userSettings,
        int amount)
    {
        this._embedAuthor = new EmbedAuthorBuilder();
        this._embed = new EmbedBuilder()
            .WithColor(DiscordConstants.LastFmColorRed);
        this._embedFooter = new EmbedFooterBuilder();

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(contextUser.SessionKeyLastFm))
        {
            sessionKey = contextUser.SessionKeyLastFm;
        }

        var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm, amount, useCache: true, sessionKey: sessionKey);

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            var errorEmbed =
                GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.Embed = errorEmbed.Build();
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(discordGuild, discordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        this._embedAuthor.WithName($"Latest tracks for {embedTitle}");

        this._embedAuthor.WithIconUrl(discordUser.GetAvatarUrl());
        this._embedAuthor.WithUrl(recentTracks.Content.UserRecentTracksUrl);
        this._embed.WithAuthor(this._embedAuthor);

        var fmRecentText = "";
        var resultAmount = recentTracks.Content.RecentTracks.Count;
        if (recentTracks.Content.RecentTracks.Any(a => a.NowPlaying))
        {
            resultAmount -= 1;
        }
        for (var i = 0; i < resultAmount; i++)
        {
            var track = recentTracks.Content.RecentTracks[i];

            if (i == 0)
            {
                if (track.AlbumCoverUrl != null)
                {
                    this._embed.WithThumbnailUrl(track.AlbumCoverUrl);
                }
            }

            var trackString = StringService.TrackToLinkedString(track, contextUser.RymEnabled);

            if (track.NowPlaying)
            {
                fmRecentText += $"🎶 - {trackString}\n";
            }
            else
            {
                fmRecentText += $"`{i + 1}` - {trackString}\n";
            }
        }

        this._embed.WithDescription(fmRecentText);

        string footerText;
        var firstTrack = recentTracks.Content.RecentTracks[0];
        if (firstTrack.NowPlaying)
        {
            footerText =
                $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles | Now Playing";
        }
        else
        {
            footerText =
                $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles";

            if (!firstTrack.NowPlaying && firstTrack.TimePlayed.HasValue)
            {
                footerText += " | Last scrobble:";
                this._embed.WithTimestamp(firstTrack.TimePlayed.Value);
            }
        }

        this._embedFooter.WithText(footerText);

        this._embed.WithFooter(this._embedFooter);

        response.Embed = this._embed.Build();
        return response;
    }

    public async Task<ResponseModel> OverviewAsync(
        IGuild discordGuild,
        IUser discordUser,
        User contextUser,
        UserSettingsModel userSettings,
        int amount)
    {
        this._embedAuthor = new EmbedAuthorBuilder();
        this._embed = new EmbedBuilder()
            .WithColor(DiscordConstants.LastFmColorRed);
        this._embedFooter = new EmbedFooterBuilder();

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        await this._updateService.UpdateUser(contextUser);

        var week = await this._playService.GetDailyOverview(userSettings.UserId, amount);

        if (week == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = "Sorry, we don't have plays for you in the selected amount of days.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        foreach (var day in week.Days.OrderByDescending(o => o.Date))
        {
            var genreString = new StringBuilder();
            if (day.TopGenres != null && day.TopGenres.Any())
            {
                for (var i = 0; i < day.TopGenres.Count; i++)
                {
                    if (i != 0)
                    {
                        genreString.Append(" - ");
                    }

                    var genre = day.TopGenres[i];
                    genreString.Append($"{genre}");
                }
            }

            this._embed.AddField(
                $"{day.Playcount} plays - {StringExtensions.GetListeningTimeString(day.ListeningTime)} - <t:{day.Date.ToUnixEpochDate()}:D>",
                $"{genreString}\n" +
                $"{day.TopArtist}\n" +
                $"{day.TopAlbum}\n" +
                $"{day.TopTrack}"
            );
        }

        var description = $"Top genres, artist, album and track for last {amount} days";

        if (week.Days.Count < amount)
        {
            description += $"\n{amount - week.Days.Count} days not shown because of no plays.";
        }

        this._embed.WithDescription(description);

        this._embedAuthor.WithName($"Daily overview for {userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}");

        this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library?date_preset=LAST_7_DAYS");
        this._embed.WithAuthor(this._embedAuthor);

        this._embedFooter.WithText($"{week.Uniques} unique tracks - {week.Playcount} total plays - avg {Math.Round(week.AvgPerDay, 1)} per day");
        this._embed.WithFooter(this._embedFooter);


        response.Embed = this._embed.Build();
        return response;
    }
}
