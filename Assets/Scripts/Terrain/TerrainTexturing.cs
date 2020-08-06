// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2020 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    Hazelnut
// 
// Notes:
//

using UnityEngine;
using System;
using DaggerfallConnect.Arena2;
using Unity.Jobs;
using Unity.Collections;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Generates texture tiles for terrains and uses marching squares for tile transitions.
    /// These features are very much in early stages of development.
    /// </summary>
    public class TerrainTexturing
    {
        // Use same seed to ensure continuous tiles
        const int seed = 417028;

        const byte water = 0;
        const byte dirt = 1;
        const byte grass = 2;
        const byte stone = 3;

        const byte road = 46;
        const byte road_grass = 55;
        const byte road_dirt = 47;

        public const byte N  = 0b1000_0000;
        public const byte NE = 0b0100_0000;
        public const byte E  = 0b0010_0000;
        public const byte SE = 0b0001_0000;
        public const byte S  = 0b0000_1000;
        public const byte SW = 0b0000_0100;
        public const byte W  = 0b0000_0010;
        public const byte NW = 0b0000_0001;


        static readonly int tileDataDim = MapsFile.WorldMapTileDim + 1;

        static readonly int assignTilesDim = MapsFile.WorldMapTileDim;

        byte[] lookupTable;

        public TerrainTexturing()
        {
            CreateLookupTable();
        }

        public JobHandle ScheduleAssignTilesJob(ITerrainSampler terrainSampler, ref MapPixelData mapData, JobHandle dependencies, bool march = true)
        {
            // Cache tile data to minimise noise sampling during march.
            NativeArray<byte> tileData = new NativeArray<byte>(tileDataDim * tileDataDim, Allocator.TempJob);
            GenerateTileDataJob tileDataJob = new GenerateTileDataJob
            {
                heightmapData = mapData.heightmapData,
                tileData = tileData,
                tdDim = tileDataDim,
                hDim = terrainSampler.HeightmapDimension,
                maxTerrainHeight = terrainSampler.MaxTerrainHeight,
                oceanElevation = terrainSampler.OceanElevation,
                beachElevation = terrainSampler.BeachElevation,
                mapPixelX = mapData.mapPixelX,
                mapPixelY = mapData.mapPixelY,
            };
            JobHandle tileDataHandle = tileDataJob.Schedule(tileDataDim * tileDataDim, 64, dependencies);

            // Assign tile data to terrain
            NativeArray<byte> lookupData = new NativeArray<byte>(lookupTable, Allocator.TempJob);
            AssignTilesJob assignTilesJob = new AssignTilesJob
            {
                lookupTable = lookupData,
                tileData = tileData,
                tilemapData = mapData.tilemapData,
                tdDim = tileDataDim,
                tDim = assignTilesDim,
                march = march,
                //mapPixelX = mapData.mapPixelX,
                //mapPixelY = mapData.mapPixelY,
                locationRect = mapData.locationRect,
                roadData = RoadPathEditor.roadData[mapData.mapPixelX + (mapData.mapPixelY * MapsFile.MaxMapPixelX)],
            };
            JobHandle assignTilesHandle = assignTilesJob.Schedule(assignTilesDim * assignTilesDim, 64, tileDataHandle);

            // Add both working native arrays to disposal list.
            mapData.nativeArrayList.Add(tileData);
            mapData.nativeArrayList.Add(lookupData);

            return assignTilesHandle;
        }

        #region Marching Squares - WIP

        // Very basic marching squares for water > dirt > grass > stone transitions.
        // Cannot handle water > grass or water > stone, etc.
        // Will improve this at later date to use a wider range of transitions.
        struct AssignTilesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> tileData;
            [ReadOnly]
            public NativeArray<byte> lookupTable;

            public NativeArray<byte> tilemapData;

            public int tdDim;
            public int tDim;
            public bool march;
            //public int mapPixelX;
            //public int mapPixelY;
            public Rect locationRect;
            public byte roadData;

            public void Execute(int index)
            {
                int x = JobA.Row(index, tDim);
                int y = JobA.Col(index, tDim);

                // Do nothing if in location rect as texture already set, to 0xFF if zero
                if (tilemapData[index] != 0)
                    return;

                //byte roadData = (byte)(1 << (mapPixelX % 8));
                //byte roadData = NE|SE; // NE|SW; //S|W; // N|E; //0b0000_0000;
                //byte roadData = GetData();
                //byte roadData = (byte)0xFF;

                if (PaintRoad(x, y, index, roadData))
                    return;

                // Assign tile texture
                if (march)
                {
                    // Get sample points
                    int tdIdx = JobA.Idx(x, y, tdDim);
                    int b0 = tileData[tdIdx];               // tileData[x, y]
                    int b1 = tileData[tdIdx + 1];           // tileData[x + 1, y]
                    int b2 = tileData[tdIdx + tdDim];       // tileData[x, y + 1]
                    int b3 = tileData[tdIdx + tdDim + 1];   // tileData[x + 1, y + 1]

                    int shape = (b0 & 1) | (b1 & 1) << 1 | (b2 & 1) << 2 | (b3 & 1) << 3;
                    int ring = (b0 + b1 + b2 + b3) >> 2;
                    int tileID = shape | ring << 4;

                    tilemapData[index] = lookupTable[tileID];
                }
                else
                {
                    tilemapData[index] = tileData[JobA.Idx(x, y, tdDim)];
                }
            }
/*
            private byte GetData()
            {
                switch ((mapPixelX + (mapPixelY * 1500)) % 8)
                {
                    case 0:
                        return NE|N;
                    case 1:
                        return NE|NE;
                    case 2:
                        return NE|E;
                    case 3:
                        return NE|SE;
                    case 4:
                        return NE|S;
                    case 5:
                        return NE|SW;
                    case 6:
                        return NE|W;
                    case 7:
                        return NE|NW;
                    default:
                        return N;
                }
            }
*/
            private bool PaintRoad(int x, int y, int index, byte roadData)
            {
                bool hasRoad = false;
/* Test locator tiles:
                if ((x == 65 && y == 65) || (x == 62 && y == 65) || (x == 65 && y == 62) || (x == 62 && y == 62))
                {
                    tilemapData[index] = water;
                    hasRoad = true;
                }*/
                // N-S
                if (((roadData & N) > 0 && (x == 63 || x == 64) && y > 63) || ((roadData & S) > 0 && (x == 63 || x == 64) && y < 64))
                {
                    tilemapData[index] = road;
                    hasRoad = true;
                }
                if (((roadData & N) > 0 && (x == 63 || x == 64) && y == 63) || ((roadData & S) > 0 && (x == 63 || x == 64) && y == 64))
                {
                    PaintHalfRoad(x, y, index, x == y, x == 64);
                    hasRoad = true;
                }
                // E-W
                if (((roadData & E) > 0 && (y == 63 || y == 64) && x > 63) || ((roadData & W) > 0 && (y == 63 || y == 64) && x < 64))
                {
                    tilemapData[index] = road;
                    hasRoad = true;
                }
                if (((roadData & E) > 0 && (y == 63 || y == 64) && x == 63) || ((roadData & W) > 0 && (y == 63 || y == 64) && x == 64))
                {
                    PaintHalfRoad(x, y, index, x == y, x == 64);
                    hasRoad = true;
                }
                // NE-SW
                if (((roadData & NE) > 0 && x == y && x > 63) || ((roadData & SW) > 0 && x == y && x < 64))
                {
                    tilemapData[index] = road;
                    hasRoad = true;
                }
                if (((roadData & NE) > 0 && x == y && x == 63) || ((roadData & SW) > 0 && x == y && x == 64))
                {
                    PaintHalfRoad(x, y, index, true, x == 64);
                    hasRoad = true;
                }
                if (((roadData & NE) > 0 && ((x == y + 1 && x > 63) || (x + 1 == y && y > 63))) || ((roadData & SW) > 0 && ((x == y + 1 && x <= 64) || (x + 1 == y && y <= 64))))
                {
                    PaintHalfRoad(x, y, index, false, (x == y + 1));
                    hasRoad = true;
                }
                // NW-SE
                int _x = 127 - x;
                if (((roadData & NW) > 0 && _x == y && x < 64) || ((roadData & SE) > 0 && _x == y && x > 63))
                {
                    tilemapData[index] = road;
                    hasRoad = true;
                }
                if (((roadData & NW) > 0 && _x == y && x == 64) || ((roadData & SE) > 0 && _x == y && x == 63))
                {
                    PaintHalfRoad(x, y, index, false, x == 64);
                    hasRoad = true;
                }
                if (((roadData & NW) > 0 && ((_x == y + 1 && x < 64) || (_x + 1 == y && y > 63))) || ((roadData & SE) > 0 && ((_x == y + 1 && x >= 63) || (_x + 1 == y && y <= 64))))
                {
                    PaintHalfRoad(x, y, index, true, (_x != y + 1));
                    hasRoad = true;
                }

                return hasRoad;
            }

            private void PaintHalfRoad(int x, int y, int index, bool rotate, bool flip)
            {
                int tileMap = tilemapData[index] & 0x3F;
                if (tileMap == road || tileMap == road_grass || tileMap == road_dirt)
                    return;

                byte tile = tileData[JobA.Idx(x, y, tdDim)];
                if (tile == grass)
                    tilemapData[index] = road_grass;
                else if (tile == dirt)
                    tilemapData[index] = road_dirt;
                else if (tile == stone)
                    tilemapData[index] = road_grass;

                if (rotate)
                    tilemapData[index] += 64;
                if (flip)
                    tilemapData[index] += 128;
            }

            private bool IsTileNotSetOrValid(byte tile)
            {
                int record = tile & 0x4F;
                return tile == 0 || record == road || record == road_grass || record == road_dirt;
            }
        }

        struct GenerateTileDataJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> heightmapData;

            public NativeArray<byte> tileData;

            public int hDim;
            public int tdDim;
            public float maxTerrainHeight;
            public float oceanElevation;
            public float beachElevation;
            public int mapPixelX;
            public int mapPixelY;

            // Gets noise value
            private float NoiseWeight(float worldX, float worldY)
            {
                return GetNoise(worldX, worldY, 0.05f, 0.9f, 0.4f, 3, seed);
            }

            // Sets texture by range
            private byte GetWeightedRecord(float weight, float lowerGrassSpread = 0.5f, float upperGrassSpread = 0.95f)
            {
                if (weight < lowerGrassSpread)
                    return dirt;
                else if (weight > upperGrassSpread)
                    return stone;
                else
                    return grass;
            }

            // Noise function
            private float GetNoise(
                float x,
                float y,
                float frequency,
                float amplitude,
                float persistance,
                int octaves,
                int seed = 0)
            {
                float finalValue = 0f;
                for (int i = 0; i < octaves; ++i)
                {
                    finalValue += Mathf.PerlinNoise(seed + (x * frequency), seed + (y * frequency)) * amplitude;
                    frequency *= 2.0f;
                    amplitude *= persistance;
                }

                return Mathf.Clamp(finalValue, -1, 1);
            }

            private bool HasRoad(int x, int y)
            {
                if (x == 64 || y == 64)
                    return true;

                if (x == y)
                {
                    return true;
                }

                if (tdDim - x - 2 == y)
                {
                    return true;
                }
                return false;
            }

            public void Execute(int index)
            {
                int x = JobA.Row(index, tdDim);
                int y = JobA.Col(index, tdDim);

/*                if (HasRoad(x, y))
                {
                    tileData[index] = road;
                    return;
                }
*/
                // Height sample for ocean and beach tiles
                int hx = (int)Mathf.Clamp(hDim * ((float)x / (float)tdDim), 0, hDim - 1);
                int hy = (int)Mathf.Clamp(hDim * ((float)y / (float)tdDim), 0, hDim - 1);
                float height = heightmapData[JobA.Idx(hy, hx, hDim)] * maxTerrainHeight;  // x & y swapped in heightmap for TerrainData.SetHeights()
                // Ocean texture
                if (height <= oceanElevation)
                {
                    tileData[index] = water;
                    return;
                }
                // Beach texture
                // Adds a little +/- randomness to threshold so beach line isn't too regular
                if (height <= beachElevation + (JobRand.Next(-15000000, 15000000) / 10000000f))
                {
                    tileData[index] = dirt;
                    return;
                }

                // Get latitude and longitude of this tile
                int latitude = (int)(mapPixelX * MapsFile.WorldMapTileDim + x);
                int longitude = (int)(MapsFile.MaxWorldTileCoordZ - mapPixelY * MapsFile.WorldMapTileDim + y);

                // Set texture tile using weighted noise
                float weight = 0;
                weight += NoiseWeight(latitude, longitude);
                // TODO: Add other weights to influence texture tile generation
                tileData[index] = GetWeightedRecord(weight);
            }
        }

        // Creates lookup table
        void CreateLookupTable()
        {
            lookupTable = new byte[64];
            AddLookupRange(0, 1, 5, 48, false, 0);
            AddLookupRange(2, 1, 10, 51, true, 16);
            AddLookupRange(2, 3, 15, 53, false, 32);
            AddLookupRange(3, 3, 15, 53, true, 48);
        }

        // Adds range of 16 values to lookup table
        void AddLookupRange(int baseStart, int baseEnd, int shapeStart, int saddleIndex, bool reverse, int offset)
        {
            if (reverse)
            {
                // high > low
                lookupTable[offset] = MakeLookup(baseStart, false, false);
                lookupTable[offset + 1] = MakeLookup(shapeStart + 2, true, true);
                lookupTable[offset + 2] = MakeLookup(shapeStart + 2, false, false);
                lookupTable[offset + 3] = MakeLookup(shapeStart + 1, true, true);
                lookupTable[offset + 4] = MakeLookup(shapeStart + 2, false, true);
                lookupTable[offset + 5] = MakeLookup(shapeStart + 1, false, true);
                lookupTable[offset + 6] = MakeLookup(saddleIndex, true, false); //d
                lookupTable[offset + 7] = MakeLookup(shapeStart, true, true);
                lookupTable[offset + 8] = MakeLookup(shapeStart + 2, true, false);
                lookupTable[offset + 9] = MakeLookup(saddleIndex, false, false); //d
                lookupTable[offset + 10] = MakeLookup(shapeStart + 1, false, false);
                lookupTable[offset + 11] = MakeLookup(shapeStart, false, false);
                lookupTable[offset + 12] = MakeLookup(shapeStart + 1, true, false);
                lookupTable[offset + 13] = MakeLookup(shapeStart, false, true);
                lookupTable[offset + 14] = MakeLookup(shapeStart, true, false);
                lookupTable[offset + 15] = MakeLookup(baseEnd, false, false);
            }
            else
            {
                // low > high
                lookupTable[offset] = MakeLookup(baseStart, false, false);
                lookupTable[offset + 1] = MakeLookup(shapeStart, true, false);
                lookupTable[offset + 2] = MakeLookup(shapeStart, false, true);
                lookupTable[offset + 3] = MakeLookup(shapeStart + 1, true, false);
                lookupTable[offset + 4] = MakeLookup(shapeStart, false, false);
                lookupTable[offset + 5] = MakeLookup(shapeStart + 1, false, false);
                lookupTable[offset + 6] = MakeLookup(saddleIndex, false, false); //d
                lookupTable[offset + 7] = MakeLookup(shapeStart + 2, true, false);
                lookupTable[offset + 8] = MakeLookup(shapeStart, true, true);
                lookupTable[offset + 9] = MakeLookup(saddleIndex, true, false); //d
                lookupTable[offset + 10] = MakeLookup(shapeStart + 1, false, true);
                lookupTable[offset + 11] = MakeLookup(shapeStart + 2, false, true);
                lookupTable[offset + 12] = MakeLookup(shapeStart + 1, true, true);
                lookupTable[offset + 13] = MakeLookup(shapeStart + 2, false, false);
                lookupTable[offset + 14] = MakeLookup(shapeStart + 2, true, true);
                lookupTable[offset + 15] = MakeLookup(baseEnd, false, false);
            }
        }

        // Encodes a byte with Daggerfall tile lookup
        byte MakeLookup(int index, bool rotate, bool flip)
        {
            if (index > 55)
                throw new IndexOutOfRangeException("Index out of range. Valid range 0-55");
            if (rotate) index += 64;
            if (flip) index += 128;

            return (byte)index;
        }

        #endregion
    }
}