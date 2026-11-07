using Anitime.Contracts;
using Anitime.Domain.Enums;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Anitime.Api.Swagger;

public sealed class ExamplesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(SignUpRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["email"] = new OpenApiString("nino@example.com"),
                ["password"] = new OpenApiString("StrongPass123"),
                ["name"] = new OpenApiString("Nino"),
                ["gender"] = new OpenApiString(nameof(Gender.Female)),
                ["dateOfBirth"] = new OpenApiString("1998-05-12")
            };
        }
        else if (context.Type == typeof(RefreshTokenRequest) || context.Type == typeof(LogoutRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["refreshToken"] = new OpenApiString("F2E4A6B8C0D2E4F6A8B0C2D4E6F8A0B2C4D6E8F0A2B4C6D8E0F2A4B6C8D0E2")
            };
        }
        else if (context.Type == typeof(CreateHabitRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["title"] = new OpenApiString("Read 20 pages"),
                ["emoji"] = new OpenApiString("📚"),
                ["startDate"] = new OpenApiString("2026-04-23"),
                ["durationDays"] = new OpenApiInteger(30)
            };
        }
        else if (context.Type == typeof(CreateTaskRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["title"] = new OpenApiString("Plan weekly meals"),
                ["description"] = new OpenApiString("Prepare healthy options for the week"),
                ["emoji"] = new OpenApiString("🥗"),
                ["date"] = new OpenApiString("2026-04-23"),
                ["clientRequestId"] = new OpenApiString("task-create-6fdd0f5f2e154dcab5d3576f5f90a9c4"),
                ["checklistItems"] = new OpenApiArray
                {
                    new OpenApiObject { ["title"] = new OpenApiString("Buy groceries") },
                    new OpenApiObject { ["title"] = new OpenApiString("Prep vegetables") }
                }
            };
        }
        else if (context.Type == typeof(CreateReminderRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["title"] = new OpenApiString("Morning workout"),
                ["message"] = new OpenApiString("Time for your 20 minute session"),
                ["scheduledForUtc"] = new OpenApiString("2026-04-24T06:30:00Z"),
                ["repeatDaily"] = new OpenApiBoolean(true)
            };
        }
        else if (context.Type == typeof(UpdateProfileRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["name"] = new OpenApiString("Nino J."),
                ["gender"] = new OpenApiString(nameof(Gender.Female)),
                ["dateOfBirth"] = new OpenApiString("1998-05-12")
            };
        }
        else if (context.Type == typeof(UpdateUserSettingsRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["backgroundStyleType"] = new OpenApiString(nameof(BackgroundStyleType.Gradient)),
                ["backgroundPrimaryColor"] = new OpenApiString("#111827"),
                ["backgroundSecondaryColor"] = new OpenApiString("#2563EB"),
                ["accentColor"] = new OpenApiString("#F59E0B"),
                ["locale"] = new OpenApiString("ka-GE"),
                ["timeZone"] = new OpenApiString("Asia/Tbilisi")
            };
        }
        else if (context.Type == typeof(CreateNoteRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["title"] = new OpenApiString("Quarterly planning"),
                ["contentJson"] = BuildRichNoteDocumentExample(),
                ["plainTextPreview"] = new OpenApiString("Goals Launch notes module Review metrics"),
                ["fontFamily"] = new OpenApiString("Georgia"),
                ["fontSize"] = new OpenApiInteger(18),
                ["fontWeight"] = new OpenApiInteger(500),
                ["textColor"] = new OpenApiString("#1F2937"),
                ["backgroundColor"] = new OpenApiString("#FFF7ED"),
                ["isPinned"] = new OpenApiBoolean(true),
                ["isArchived"] = new OpenApiBoolean(false)
            };
        }
        else if (context.Type == typeof(UpdateNoteRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["title"] = new OpenApiString("Quarterly planning"),
                ["contentJson"] = BuildRichNoteDocumentExample(),
                ["plainTextPreview"] = new OpenApiString("Goals Launch notes module Review metrics"),
                ["fontFamily"] = new OpenApiString("Georgia"),
                ["fontSize"] = new OpenApiInteger(18),
                ["fontWeight"] = new OpenApiInteger(500),
                ["textColor"] = new OpenApiString("#1F2937"),
                ["backgroundColor"] = new OpenApiString("#FFF7ED"),
                ["isPinned"] = new OpenApiBoolean(true),
                ["isArchived"] = new OpenApiBoolean(false),
                ["versionToken"] = new OpenApiString("6eb1b66ac9e84c28b560d96885c77e84")
            };
        }
        else if (context.Type == typeof(UpdateNoteContentRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["contentJson"] = BuildRichNoteDocumentExample(),
                ["plainTextPreview"] = new OpenApiString("Goals Launch notes module Review metrics"),
                ["versionToken"] = new OpenApiString("6eb1b66ac9e84c28b560d96885c77e84")
            };
        }
        else if (context.Type == typeof(AuthResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["userId"] = new OpenApiString("87503ea8-72eb-4d2a-b95c-68246c5295ec"),
                ["email"] = new OpenApiString("nino@example.com"),
                ["name"] = new OpenApiString("Nino"),
                ["gender"] = new OpenApiString(nameof(Gender.Female)),
                ["dateOfBirth"] = new OpenApiString("1998-05-12"),
                ["accessToken"] = new OpenApiString("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sample"),
                ["expiresAtUtc"] = new OpenApiString("2026-05-04T10:00:00Z"),
                ["refreshToken"] = new OpenApiString("F2E4A6B8C0D2E4F6A8B0C2D4E6F8A0B2C4D6E8F0A2B4C6D8E0F2A4B6C8D0E2"),
                ["refreshTokenExpiresAtUtc"] = new OpenApiString("2026-08-02T10:00:00Z")
            };
        }
        else if (context.Type == typeof(UserProfileResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["userId"] = new OpenApiString("87503ea8-72eb-4d2a-b95c-68246c5295ec"),
                ["email"] = new OpenApiString("nino@example.com"),
                ["name"] = new OpenApiString("Nino J."),
                ["gender"] = new OpenApiString(nameof(Gender.Female)),
                ["dateOfBirth"] = new OpenApiString("1998-05-12"),
                ["age"] = new OpenApiInteger(27),
                ["profileImageUrl"] = new OpenApiString("https://api.anitime.space/api/profile/profile-image"),
                ["backgroundImageUrl"] = new OpenApiString("https://api.anitime.space/api/profile/background-image"),
                ["settings"] = new OpenApiObject
                {
                    ["backgroundStyleType"] = new OpenApiString(nameof(BackgroundStyleType.Gradient)),
                    ["backgroundPrimaryColor"] = new OpenApiString("#111827"),
                    ["backgroundSecondaryColor"] = new OpenApiString("#2563EB"),
                    ["accentColor"] = new OpenApiString("#F59E0B"),
                    ["locale"] = new OpenApiString("ka-GE"),
                    ["timeZone"] = new OpenApiString("Asia/Tbilisi")
                }
            };
        }
        else if (context.Type == typeof(HabitResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("f3fe5618-5037-49cf-8f0e-f3d72dc2c9d5"),
                ["title"] = new OpenApiString("Read 20 pages"),
                ["emoji"] = new OpenApiString("📚"),
                ["startDate"] = new OpenApiString("2026-04-01"),
                ["durationDays"] = new OpenApiInteger(30),
                ["currentStreak"] = new OpenApiInteger(5),
                ["bestStreak"] = new OpenApiInteger(8),
                ["completedDays"] = new OpenApiInteger(12),
                ["entries"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"] = new OpenApiString("e1db2440-8815-4f0a-ae06-5da94a8f8342"),
                        ["date"] = new OpenApiString("2026-04-21"),
                        ["isCompleted"] = new OpenApiBoolean(true)
                    }
                }
            };
        }
        else if (context.Type == typeof(TaskResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("d3be7a4f-cfbe-492e-ae06-7be8de68bb3e"),
                ["title"] = new OpenApiString("Plan weekly meals"),
                ["description"] = new OpenApiString("Prepare healthy options for the week"),
                ["emoji"] = new OpenApiString("🥗"),
                ["date"] = new OpenApiString("2026-04-23"),
                ["isCompleted"] = new OpenApiBoolean(false),
                ["checklistItems"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"] = new OpenApiString("86dc3156-d55e-42ab-a941-6ce3d65c8d54"),
                        ["title"] = new OpenApiString("Buy groceries"),
                        ["isDone"] = new OpenApiBoolean(true)
                    }
                }
            };
        }
        else if (context.Type == typeof(DeleteTaskResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("d3be7a4f-cfbe-492e-ae06-7be8de68bb3e")
            };
        }
        else if (context.Type == typeof(ReminderResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("c8073df3-4d7f-4b67-af14-b4863549f1fb"),
                ["title"] = new OpenApiString("Morning workout"),
                ["message"] = new OpenApiString("Time for your 20 minute session"),
                ["scheduledForUtc"] = new OpenApiString("2026-04-24T06:30:00Z"),
                ["repeatDaily"] = new OpenApiBoolean(true),
                ["isEnabled"] = new OpenApiBoolean(true),
                ["lastTriggeredAtUtc"] = new OpenApiNull()
            };
        }
        else if (context.Type == typeof(DashboardResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["totalHabits"] = new OpenApiInteger(4),
                ["activeHabits"] = new OpenApiInteger(3),
                ["habitsCompletedToday"] = new OpenApiInteger(2),
                ["totalTasks"] = new OpenApiInteger(10),
                ["tasksCompletedToday"] = new OpenApiInteger(4),
                ["pendingTasks"] = new OpenApiInteger(6),
                ["bestHabitStreak"] = new OpenApiInteger(12),
                ["currentHabitStreak"] = new OpenApiInteger(5)
            };
        }
        else if (context.Type == typeof(AnalyticsResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["from"] = new OpenApiString("2026-03-25"),
                ["to"] = new OpenApiString("2026-04-23"),
                ["habitCompletionRate"] = new OpenApiDouble(74.32),
                ["taskCompletionRate"] = new OpenApiDouble(61.54),
                ["bestStreak"] = new OpenApiInteger(12),
                ["daily"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["date"] = new OpenApiString("2026-04-23"),
                        ["habitCompletions"] = new OpenApiInteger(2),
                        ["tasksCompleted"] = new OpenApiInteger(1),
                        ["tasksPlanned"] = new OpenApiInteger(3)
                    }
                }
            };
        }
        else if (context.Type == typeof(CalendarMonthResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["year"] = new OpenApiInteger(2026),
                ["month"] = new OpenApiInteger(4),
                ["days"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["date"] = new OpenApiString("2026-04-23"),
                        ["habitCompletions"] = new OpenApiInteger(2),
                        ["tasksDue"] = new OpenApiInteger(3),
                        ["tasksCompleted"] = new OpenApiInteger(1)
                    }
                }
            };
        }
        else if (context.Type == typeof(NoteResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("d4c9a768-a1cd-4c86-a971-e6a7d7cb09f6"),
                ["title"] = new OpenApiString("Quarterly planning"),
                ["contentJson"] = BuildRichNoteDocumentExample(),
                ["plainTextPreview"] = new OpenApiString("Goals Launch notes module Review metrics"),
                ["fontFamily"] = new OpenApiString("Georgia"),
                ["fontSize"] = new OpenApiInteger(18),
                ["fontWeight"] = new OpenApiInteger(500),
                ["textColor"] = new OpenApiString("#1F2937"),
                ["backgroundColor"] = new OpenApiString("#FFF7ED"),
                ["isPinned"] = new OpenApiBoolean(true),
                ["isArchived"] = new OpenApiBoolean(false),
                ["versionToken"] = new OpenApiString("6eb1b66ac9e84c28b560d96885c77e84"),
                ["lastEditedAtUtc"] = new OpenApiString("2026-04-27T09:35:00Z"),
                ["createdAtUtc"] = new OpenApiString("2026-04-27T09:10:00Z"),
                ["updatedAtUtc"] = new OpenApiString("2026-04-27T09:35:00Z")
            };
        }
        else if (context.Type == typeof(ApiErrorResponse))
        {
            schema.Example = new OpenApiObject
            {
                ["code"] = new OpenApiString("validation_failed"),
                ["message"] = new OpenApiString("One or more validation errors occurred."),
                ["errors"] = new OpenApiObject
                {
                    ["Email"] = new OpenApiArray
                    {
                        new OpenApiString("Email is required.")
                    }
                },
                ["traceId"] = new OpenApiString("00-7d7f7f7f7f7f7f7f7d7f7f7f7f7f7f7f-7d7f7f7f7f7f7f7f-00")
            };
        }
    }

    private static OpenApiObject BuildRichNoteDocumentExample() =>
        new()
        {
            ["version"] = new OpenApiInteger(1),
            ["blocks"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["type"] = new OpenApiString("heading"),
                    ["align"] = new OpenApiString("left"),
                    ["children"] = new OpenApiArray
                    {
                        new OpenApiObject
                        {
                            ["textNode"] = new OpenApiObject
                            {
                                ["type"] = new OpenApiString("text"),
                                ["text"] = new OpenApiString("Goals"),
                                ["marks"] = new OpenApiArray
                                {
                                    new OpenApiObject { ["type"] = new OpenApiString("bold") },
                                    new OpenApiObject { ["type"] = new OpenApiString("fontSize"), ["value"] = new OpenApiString("24") }
                                }
                            }
                        }
                    }
                },
                new OpenApiObject
                {
                    ["type"] = new OpenApiString("paragraph"),
                    ["align"] = new OpenApiString("left"),
                    ["children"] = new OpenApiArray
                    {
                        new OpenApiObject
                        {
                            ["textNode"] = new OpenApiObject
                            {
                                ["type"] = new OpenApiString("text"),
                                ["text"] = new OpenApiString("Launch notes module"),
                                ["marks"] = new OpenApiArray
                                {
                                    new OpenApiObject { ["type"] = new OpenApiString("italic") },
                                    new OpenApiObject { ["type"] = new OpenApiString("textColor"), ["value"] = new OpenApiString("#1F2937") }
                                }
                            }
                        }
                    }
                }
            }
        };
}
