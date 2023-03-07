﻿using System.Collections.Generic;
using Discord;
using Discord.WebSocket;

namespace FMBot.Bot.Models.MusicBot;

public abstract class MusicBot
{
    public string Name { get; }
    public bool PossiblyIncludesLinks { get; }

    public bool SkipUploaderName { get; }

    public static IEnumerable<MusicBot> SupportedBots { get; } = new List<MusicBot>
    {
        new JockieMusicBot(),
        new CakeyBotMusicBot(),
        new SoundCloudMusicBot(),
        new HydraMusicBot()
    };

    protected MusicBot(string name, bool possiblyIncludesLinks = true, bool skipUploaderName = false)
    {
        this.Name = name;
        this.PossiblyIncludesLinks = possiblyIncludesLinks;
        this.SkipUploaderName = skipUploaderName;
    }

    public bool IsAuthor(SocketUser user)
    {
        return user?.Username?.StartsWith(this.Name) ?? false;
    }

    public abstract bool ShouldIgnoreMessage(IUserMessage msg);

    public abstract string GetTrackQuery(IUserMessage msg);
}
