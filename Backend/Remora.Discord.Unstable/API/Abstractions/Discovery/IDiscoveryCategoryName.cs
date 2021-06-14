//
//  IDiscoveryCategoryName.cs
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

using System.Collections.Generic;
using JetBrains.Annotations;

namespace Remora.Discord.API.Abstractions
{
    /// <summary>
    /// Represents a Discovery category name.
    /// </summary>
    [PublicAPI]
    public interface IDiscoveryCategoryName
    {
        /// <summary>
        /// Gets the category name in english.
        /// </summary>
        string Default { get; }

        /// <summary>
        /// Gets a map of localized names, where the key is the country code of the localized name.
        /// </summary>
        IReadOnlyDictionary<string, string> Localizations { get; }
    }
}
