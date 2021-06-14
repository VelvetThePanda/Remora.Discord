//
//  IDiscoveryMetadata.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Remora.Discord.Core;

namespace Remora.Discord.API.Abstractions
{
    /// <summary>
    /// Represents a guild's Discovery settings.
    /// </summary>
    [PublicAPI]
    public interface IDiscoveryMetadata
    {
        /// <summary>
        /// Gets the ID of the guild.
        /// </summary>
        Snowflake GuildID { get; }

        /// <summary>
        /// Gets the ID of the primary discovery category set for this guild.
        /// </summary>
        int PrimaryCategoryID { get; }

        /// <summary>
        /// Gets up to 10 search keywords for this guild.
        /// </summary>
        IReadOnlyList<string>? Keywords { get; }

        /// <summary>
        /// Gets a value indicating whether guild information is shown when custom emojis from this guild are clicked.
        /// </summary>
        bool IsEmojiDiscoverabilityEnabled { get; }

        /// <summary>
        /// Gets the time when the server's partner application was accepted or denied.
        /// </summary>
        DateTimeOffset? PartnerActionedTimestamp { get; }

        /// <summary>
        /// Gets the time when the server applied for partnership.
        /// </summary>
        DateTimeOffset? PartnerApplicationTimestamp { get; }

        /// <summary>
        /// Gets up to 5 Discovery subcategories for this guild.
        /// </summary>
        IReadOnlyList<int> CategoryIDs { get; }
    }
}
