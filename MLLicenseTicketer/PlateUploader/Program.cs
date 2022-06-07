using System.Net;

namespace PlateUploader;
using System;
using System.IO;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;


public class PlateUploader
{
    static void Main(string[] args)
    {
        // Capture path from args array
        string path = @args[0];

        // Use FileInfo object to verify if the filepath points to an existing file.
        FileInfo file = new FileInfo(path);
        if (!file.Exists)
        {
            // Throw new exception if the file does not exist.
            throw new FileNotFoundException("File does not exist.");
        }

        // Split the path on '.' to get the filetype extension.
        string[] type = path.Split('.');

        // Check if it matches the accepted filetype (.jpg).
        if (!String.Equals(type[1], "jpg"))
        {
            // Throw an exception if it does not match.
            throw new ArgumentException("File type must be a jpg image.");
        }
        
        // If the arguments pass checks, use UploadFile method
        UploadFile(path, "cc-plate-bucket").Wait();
        Console.WriteLine("Done.");
    }

    /// <summary>
    /// Get AWS Credentials by profile name.
    /// </summary>
    /// <param name="profileName">The name of the profile.</param>
    /// <returns>The AWS Credentials</returns>
    /// <exception cref="ArgumentNullException">Profile cannot be null or empty.</exception>
    /// <exception cref="Exception">Profile not found.</exception>
    private static AWSCredentials GetAwsCredentials(string profileName)
    {
        if (String.IsNullOrEmpty(profileName))
        {
            throw new ArgumentNullException("profileName cannot be null or empty.");
        }

        SharedCredentialsFile credFile = new SharedCredentialsFile();
        CredentialProfile profile = credFile.ListProfiles().Find(p => p.Name.Equals(profileName));
        if (profile == null)
        {
            throw new Exception(String.Format("Profile named {0} not found.", profileName));
        }

        return AWSCredentialsFactory.GetAWSCredentials(profile, new SharedCredentialsFile());
    }

    private static async Task UploadFile(string filePath, string bucketName)
    {
        
        // If the arguments pass checks, create credentials file and S3Client objects
        AWSCredentials credentials = GetAwsCredentials("default");

        AmazonS3Client s3Client = new AmazonS3Client(credentials, RegionEndpoint.USEast1);

        List<string> ticket = TicketRandomizer();

        try
        {
            PutObjectRequest putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                FilePath = filePath,
            };

            putRequest.Metadata.Add("Location", ticket[0]);
            putRequest.Metadata.Add("Date", ticket[1]);
            putRequest.Metadata.Add("Violation", ticket[2]);

            PutObjectResponse response = await s3Client.PutObjectAsync(putRequest);
            Console.WriteLine("File uploading completed.");

            s3Client.Dispose();
        }
        catch (AmazonS3Exception e)
        {
            if (e.ErrorCode != null &&
                (e.ErrorCode.Equals("InvalidAccessKeyId")
                 ||
                 e.ErrorCode.Equals("InvalidSecurity")))
            {
                throw new Exception("Check the provided AWS Credentials.");
            }
            else
            {
                throw new Exception("Error occurred: " + e.Message);
            }
        }
    }

    private static List<string> TicketRandomizer()
    {
        var violationType = new List<string>()
        {
            {"No stop."},
            {"No full stop on right."},
            {"No right on red."}
        };

        var locations = new List<string>()
        {
            "Main St and 116th Ave intersection, Bellevue",
            "145th and Greenwood intersection, Shoreline",
            "45th and Stone Way intersection, Seattle"
        };

        Random rand = new Random();

        int index = rand.Next(2);

        var violation = violationType[index];

        index = rand.Next(2);

        var location = locations[index];

        string date = DateTime.Now.ToLongDateString();
        string time = DateTime.Now.ToLocalTime().ToString("h:mm:ss tt");

        string dateTime = date + " " + time;

        return new List<string>()
        {
            {location},
            {dateTime},
            {violation}
        };
    }
}