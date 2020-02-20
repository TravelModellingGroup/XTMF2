/*
    Copyright 2017 University of Toronto

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
using System.Text;

namespace XTMF2
{
    public readonly struct Rectangle
    {
        /// <summary>
        /// A rectangle that represents a node that will not be shown.
        /// </summary>
        public static Rectangle Hidden { get; } = new Rectangle(-1, -1, -1, -1);

        public Rectangle(float x, float y, float width = 80, float height = 50)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public override bool Equals(object? obj)
        {
            if(obj is Rectangle other)
            {
                return other.X == X & other.Y == Y & other.Width == Width & other.Height == Height;
            }
            return false;
        }

        public bool Equals(Rectangle other)
        {
            return other.X == X & other.Y == Y & other.Width == Width & other.Height == Height;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Width, Height);
        }

        public static bool operator ==(Rectangle left, Rectangle right)
        {
            return left.X == right.X & left.Y == right.Y & left.Width == right.Width & left.Height == right.Height;
        }

        public static bool operator !=(Rectangle left, Rectangle right)
        {
            return left.X != right.X | left.Y != right.Y | left.Width != right.Width | left.Height != right.Height;
        }

        public override string ToString()
        {
            return $"{{{X},{Y},{Width},{Height}}}";
        }
    }
}
