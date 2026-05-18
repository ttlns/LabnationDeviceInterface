using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

namespace MatlabFileIO
{
    public class MatlabFileWriter
    {
        BinaryWriter fileWriter;
        MemoryStream uncompressedStream;
        IMatlabFileWriterLocker locker;

        public MatlabFileWriter(string fileName)
        {
            //create stream from filename
            FileStream fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            this.fileWriter = new BinaryWriter(fileStream);

            //write .mat file header (128 bytes)
            fileWriter.WriteMatlabHeader();
        }

        public void Close()
        {
            Flush();
            fileWriter.Close();
        }

        private void Flush()
        {
            if(uncompressedStream == null)
                return;

            // Use DeflateStream type matching our update in MatfileHelper
            fileWriter.Write(MatfileHelper.MatlabDataTypeNumber(typeof(DeflateStream)));
            
            byte[] rawBytes = uncompressedStream.ToArray();
            byte[] compressedBuffer;

            using (MemoryStream msCompressed = new MemoryStream())
            {
                // 1. Write the standard ZLIB Header bytes (Default compression level marker)
                msCompressed.WriteByte(0x78);
                msCompressed.WriteByte(0x9C);

                // 2. Compress the actual payload using standard Deflate
                using (DeflateStream deflateStream = new DeflateStream(msCompressed, CompressionMode.Compress, true))
                {
                    deflateStream.Write(rawBytes, 0, rawBytes.Length);
                }

                // 3. Calculate and append the Adler-32 Checksum footer required by ZLIB spec
                uint adler = CalculateAdler32(rawBytes);
                msCompressed.WriteByte((byte)((adler >> 24) & 0xFF));
                msCompressed.WriteByte((byte)((adler >> 16) & 0xFF));
                msCompressed.WriteByte((byte)((adler >> 8) & 0xFF));
                msCompressed.WriteByte((byte)(adler & 0xFF));

                compressedBuffer = msCompressed.ToArray();
            }

            fileWriter.Write((UInt32)compressedBuffer.Length);
            fileWriter.Write(compressedBuffer);

            uncompressedStream = null;
        }

        // Helper method to compute Adler-32 checksum manually without external libraries
        private static uint CalculateAdler32(byte[] data)
        {
            uint s1 = 1;
            uint s2 = 0;
            for (int i = 0; i < data.Length; i++)
            {
                s1 = (s1 + data[i]) % 65521;
                s2 = (s2 + s1) % 65521;
            }
            return (s2 << 16) | s1;
        }

        public MatLabFileArrayWriter OpenArray(Type t, string varName, bool compress)
        {
            //first check if there is no array operation ongoing
            if (locker != null)
            {
                if(!locker.HasFinished())
                    throw new Exception("Previous array still open!");
                Flush();
            }

            //check whether type is not a string, as this is not supported for now
            if (t.Equals(typeof(String)))
                throw new NotImplementedException("Writing arrays of strings is not supported (as strings are arrays already)");
                
            MatLabFileArrayWriter arrayWriter;
            if(compress) {
                uncompressedStream = new MemoryStream();
                arrayWriter = new MatLabFileArrayWriter(t, varName, new BinaryWriter(uncompressedStream));
            } else {
                arrayWriter = new MatLabFileArrayWriter(t, varName, fileWriter);
            }

            locker = (IMatlabFileWriterLocker)arrayWriter;
            return arrayWriter;
        }

        public void Write(string name, object data, bool compress = true)
        {
            //let's write some data
            if(data.GetType().Equals
                (typeof(String))) {
                    //a string is considered an array of chars
                    string dataAsString = data as string;
                    MatLabFileArrayWriter charArrayWriter = OpenArray(typeof(char), name, compress);
                    charArrayWriter.AddRow(dataAsString.ToCharArray());
                    charArrayWriter.FinishArray(typeof(char));
            }
            else{
                Type t;
                MatLabFileArrayWriter arrayWriter;
                if (data.GetType().IsArray && (data as Array).Rank > 1) //Handle multidimensional array
                {
                    Array arr = data as Array;
                    if (arr.Rank > 2)
                        throw new Exception("Matlab write doesn't support multidimensional arrays with a rank > 2");
                    t = arr.GetValue(0, 0).GetType();
                    arrayWriter = OpenArray(t, name, compress);
                    for (int i = 0; i < arr.GetLength(0); i++)
                    {
                        arrayWriter.AddRow(arr.SliceRow(i));
                    }
                }
                else
                {
                    if (data.GetType().IsArray)
                        t = (data as Array).GetValue(0).GetType();
                    else
                        t = data.GetType();
                    arrayWriter = OpenArray(t, name, compress);
                    arrayWriter.AddRow(data);
                }
                arrayWriter.FinishArray(t);
            }
        }
    }
}
