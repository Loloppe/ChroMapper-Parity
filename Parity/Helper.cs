using Beatmap.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Parity
{
    internal class Helper
    {
        public static void HandlePattern(List<BaseNote> cubes)
        {
            var length = 0;
            var timeGroupedCubes = cubes.GroupBy(x => x.JsonTime).ToDictionary(x => x.Key, x => x.ToArray());

            for (int n = 0; n < cubes.Count - 2; n++)
            {
                if (length > 0)
                {
                    length--;
                    continue;
                }

                BaseNote cube = cubes[n];
                if (cube.JsonTime == cubes[n + 1].JsonTime)
                {
                    // Pattern found
                    BaseNote[] cubesAtCurrentTime = timeGroupedCubes[cube.JsonTime];
                    length = cubesAtCurrentTime.Length - 1;
                    BaseNote arrowLastElement = cubesAtCurrentTime.LastOrDefault(c => c.CutDirection != 8);
                    double direction = 0;
                    if (arrowLastElement is null)
                    {
                        // Pattern got no arrow
                        var foundArrowIndex = cubes.FindIndex(c => c.CutDirection != 8 && c.JsonTime > cube.JsonTime);

                        if (foundArrowIndex != -1)
                        {
                            var foundArrow = cubes[foundArrowIndex];
                            // An arrow note is found after the note
                            direction = ReverseCutDirection(Mod(DirectionToDegree[foundArrow.CutDirection] + foundArrow.AngleOffset, 360));
                            for (int i = foundArrowIndex - 1; i > n; i--)
                            {
                                // Reverse for every dot note in between
                                if (cubes[i + 1].JsonTime - cubes[i].JsonTime >= 0.25)
                                {
                                    direction = ReverseCutDirection(direction);
                                }
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Use the arrow to determine the direction
                        direction = ReverseCutDirection(Mod(DirectionToDegree[arrowLastElement.CutDirection] + arrowLastElement.AngleOffset, 360));
                    }
                    // Simulate a swing to determine the entry point of the pattern
                    (double x, double y) pos;
                    if (n > 0)
                    {
                        pos = SimSwingPos(cubes[n - 1].PosX, cubes[n - 1].PosY, direction);
                    }
                    else
                    {
                        pos = SimSwingPos(cubes[0].PosX, cubes[0].PosY, direction);
                    }
                    // Calculate the distance of each note based on the new position
                    List<double> distance = new();
                    for (int i = n; i < n + length + 1; i++)
                    {
                        distance.Add(Math.Sqrt(Math.Pow(pos.y - cubes[i].PosY, 2) + Math.Pow(pos.x - cubes[i].PosX, 2)));
                    }
                    // Re-order the notes in the proper order
                    for (int i = 0; i < distance.Count; i++)
                    {
                        for (int j = n; j < n + length; j++)
                        {
                            if (distance[j - n + 1] < distance[j - n])
                            {
                                Swap(cubes, j, j + 1);
                                Swap(distance, j - n + 1, j - n);
                            }
                        }
                    }
                }
            }
        }

        public static int[] DirectionToDegree = { 90, 270, 180, 0, 135, 45, 225, 315, 270 };

        public static (double x, double y) SimSwingPos(double x, double y, double direction, double dis = 5)
        {
            return (x + dis * Math.Cos(ConvertDegreesToRadians(direction)), y + dis * Math.Sin(ConvertDegreesToRadians(direction)));
        }

        public static double ConvertDegreesToRadians(double degrees)
        {
            double radians = degrees * (Math.PI / 180f);
            return radians;
        }

        public static double ConvertRadiansToDegrees(double radians)
        {
            double degrees = radians * (180f / Math.PI);
            return degrees;
        }

        public static double ReverseCutDirection(double direction)
        {
            if (direction >= 180)
            {
                return direction - 180;
            }
            else
            {
                return direction + 180;
            }
        }
        public static double Mod(double x, double m)
        {
            return (x % m + m) % m;
        }

        public static void Swap<T>(IList<T> list, int indexA, int indexB)
        {
            (list[indexB], list[indexA]) = (list[indexA], list[indexB]);
        }

        public static bool IsSameDir(double before, double after, double degree = 67.5)
        {
            before = Mod(before, 360);
            after = Mod(after, 360);

            if (Math.Abs(before - after) <= 180)
            {
                if (Math.Abs(before - after) < degree)
                {
                    return true;
                }
            }
            else
            {
                if (360 - Math.Abs(before - after) < degree)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
