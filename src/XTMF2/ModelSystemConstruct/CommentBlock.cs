/*
    Copyright 2019 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using XTMF2.Editing;

namespace XTMF2.ModelSystemConstruct
{
    /// <summary>
    /// This class is used to add comments inside of a boundary
    /// </summary>
    public sealed class CommentBlock : INotifyPropertyChanged
    {
        private const string XProperty = "X";
        private const string YProperty = "Y";
        private const string WidthProperty = "Width";
        private const string HeightProperty = "Height";
        private const string CommentProperty = "Comment";

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Construct a new comments block
        /// </summary>
        /// <param name="comment"></param>
        /// <param name="location"></param>
        public CommentBlock(string comment, Rectangle location)
        {
            _comment = comment;
            _location = location;
        }

        /// <summary>
        /// Invoke this when a property is changed
        /// </summary>
        /// <param name="propertyName">The name of the property that was changed</param>
        private void Notify(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private Rectangle _location;
        private string _comment;

        /// <summary>
        /// The location to place the Comment Block within the boundary
        /// </summary>
        public Rectangle Location
        {
            get => _location;
            set
            {
                _location = value;
                Notify(nameof(Location));
            }
        }

        /// <summary>
        /// The comment string to display
        /// </summary>
        public string Comment
        {
            get => _comment;
            set
            {
                _comment = value;
                Notify(nameof(Comment));
            }
        }

        internal void Save(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(XProperty, Location.X);
            writer.WriteNumber(YProperty, Location.Y);
            writer.WriteNumber(WidthProperty, Location.Width);
            writer.WriteNumber(HeightProperty, Location.Height);
            writer.WriteString(CommentProperty, Comment);
            writer.WriteEndObject();
        }

        internal static bool Load(ref Utf8JsonReader reader, out CommentBlock? block, ref string? error)
        {
            float x = 0, y = 0, width = 0, height = 0;
            string comment = "No comment";
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.Comment)
                {
                    continue;
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals(XProperty))
                    {
                        reader.Read();
                        x = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(YProperty))
                    {
                        reader.Read();
                        y = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(WidthProperty))
                    {
                        reader.Read();
                        width = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(HeightProperty))
                    {
                        reader.Read();
                        height = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(CommentProperty))
                    {
                        reader.Read();
                        comment = reader.GetString();
                    }
                }
            }
            block = new CommentBlock(comment, new Rectangle(x, y, width, height));
            return true;
        }
    }
}
