using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.Translate;
using Amazon.Translate.Model;
using System.Net;
using System.Net.Mail;
using System.Text.Json;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TicketProcessingFunction;

public class Function
{
    const string UpwardQueueUri = "https://sqs.us-east-1.amazonaws.com/926831757693/upwardQueue";
    SessionAWSCredentials sessionCredentials = new SessionAWSCredentials("awsAccessKeyId", "awsSecretAccessKey", "token");

     Dictionary<string, string> languageCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Afrikaans", "af"                },
        { "Albanian","sq"                   },
        { "Amharic","am"                    },
        { "Arabic","ar"                     },
        { "Armenian","hy"                   },
        { "Azerbaijani","az"                },
        { "Bengali","bn"                    },
        { "Bosnian","bs"                    },
        { "Bulgarian","bg"                  },
        { "Catalan","ca"                    },
        { "Chinese (Simplified)","zh"       },
        { "Chinese (Traditional)","zh-TW"   },
        { "Croatian","hr"                   },
        { "Czech","cs"                      },
        { "Danish","da"                     },
        { "Dari","fa-AF"                    },
        { "Dutch","nl"                      },
        { "English","en"                    },
        { "Estonian","et"                   },
        { "Farsi (Persian)","fa"            },
        { "Filipino, Tagalog","tl"          },
        { "Finnish","fi"                    },
        { "French","fr"                     },
        { "French (Canada)","fr-CA"         },
        { "Georgian","ka"                   },
        { "German","de"                     },
        { "Greek","el"                      },
        { "Gujarati","gu"                   },
        { "Haitian Creole","ht"             },
        { "Hausa","ha"                      },
        { "Hebrew","he"                     },
        { "Hindi","hi"                      },
        { "Hungarian","hu"                  },
        { "Icelandic","is"                  },
        { "Indonesian","id"                 },
        { "Irish","ga"                      },
        { "Italian","it"                    },
        { "Japanese","ja"                   },
        { "Kannada","kn"                    },
        { "Kazakh","kk"                     },
        { "Korean","ko"                     },
        { "Latvian","lv"                    },
        { "Lithuanian","lt"                 },
        { "Macedonian","mk"                 },
        { "Malay","ms"                      },
        { "Malayalam","ml"                  },
        { "Maltese","mt"                    },
        { "Marathi","mr"                    },
        { "Mongolian","mn"                  },
        { "Norwegian","no"                  },
        { "Pashto","ps"                     },
        { "Polish","pl"                     },
        { "Portuguese (Brazil)","pt"        },
        { "Portuguese (Portugal)","pt-PT"   },
        { "Punjabi","pa"                    },
        { "Romanian","ro"                   },
        { "Russian","ru"                    },
        { "Serbian","sr"                    },
        { "Sinhala","si"                    },
        { "Slovak","sk"                     },
        { "Slovenian","sl"                  },
        { "Somali","so"                     },
        { "Spanish","es"                    },
        { "Spanish (Mexico)","es-MX"        },
        { "Swahili","sw"                    },
        { "Swedish","sv"                    },
        { "Tamil","ta"                      },
        { "Telugu","te"                     },
        { "Thai","th"                       },
        { "Turkish","tr"                    },
        { "Ukrainian","uk"                  },
        { "Urdu","ur"                       },
        { "Uzbek","uz"                      },
        { "Vietnamese","vi"                 },
        { "Welsh","cy"                      }
    };

    public Function()
    {

    }


    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
    /// to respond to SQS messages.
    /// </summary>
    /// <param name="evnt"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        foreach(var message in evnt.Records)
        {
            await ProcessMessageAsync(message, context);
        }
    }

    private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
    {
        context.Logger.LogInformation($"Processing message {message.Body}");

        AmazonSQSClient sqsClient = new AmazonSQSClient(sessionCredentials, Amazon.RegionEndpoint.USEast1);
        await sqsClient.DeleteMessageAsync(UpwardQueueUri, message.ReceiptHandle);
        context.Logger.LogInformation($"Deleted message from queue.");

        TrafficViolation violation = ParseTrafficViolation(message.Body);

        string emailBody = ConstructEmailBody(violation);

        SendEmailNotification(violation.Contact, emailBody);

        await Task.CompletedTask;
    }

    private void SendEmailNotification(string toAddress, string emailBody)
    {
        MailMessage message = new MailMessage();
        message.From = new MailAddress("jessicaramoscortes@gmail.com");
        message.To.Add(new MailAddress(toAddress));
        message.Subject = "You just got served";
        // ^^^ lol
        message.Body = emailBody;

        SmtpClient smtpClient = new SmtpClient("smtp.gmail.com", 587);
        smtpClient.EnableSsl = true;
        smtpClient.UseDefaultCredentials = false;
        smtpClient.Credentials = 
            new NetworkCredential("fakedmvemail@gmail.com", "cs455p@ssword");
        smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

        try
        {
            smtpClient.Send(message);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error sending email message");
            Console.WriteLine(e.Message);
        }
    }

    private string ConstructEmailBody(TrafficViolation violation)
    {
        string? explanation = "Your vehicle was involved in a traffic violation. Please pay the specified ticket amount by 30 days: ";

        if (languageCodes[violation.PreferredLanguage] != "en")
        {
            var translate = new AmazonTranslateClient(sessionCredentials, RegionEndpoint.USEast1);
            var request = new TranslateTextRequest() { Text = explanation, SourceLanguageCode = "en", TargetLanguageCode = languageCodes[violation.PreferredLanguage] };
            TranslateTextResponse? translateResponse = null;
            try
            {
                translateResponse = translate.TranslateTextAsync(request).Result;
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to translate text via AWS");
                Console.WriteLine(e.Message);
            }
            explanation = translateResponse?.TranslatedText;
        }

        string result = explanation + "\n\n";
        result += $"Vehicle: {violation.Color} {violation.Make} {violation.Model}\n";
        result += $"License plate: {violation.Plate}\n";
        result += $"Date: {violation.Date}\n";
        result += $"Violation address: {violation.ViolationLocation}\n";
        result += $"Violation type: {violation.ViolationType}\n";
        result += $"Ticket amount: {violation.TicketAmount}\n";

        return result;
    }

    private TrafficViolation ParseTrafficViolation(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Event body was null.");
        }

        TrafficViolation? result = null;
        try
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            result = JsonSerializer.Deserialize<TrafficViolation?>(body, options);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error while parsing traffic violation json.");
            Console.WriteLine(e.Message);
        }

        if (result == null)
        {
            throw new InvalidOperationException("Violation was null after parsing.");
        }

        return result;
    }
}