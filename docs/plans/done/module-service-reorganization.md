# Module & Service Directory Reorganization

## Status: In Progress

## Summary

Reorganize flat Modules/ and Services/ directories into domain-based subdirectories. Namespaces will match directory structure.

## Modules Layout

```
Modules/
├── Profiles/
│   ├── ProfileModule.cs
│   ├── RankModule.cs
│   └── BirthdayModule.cs
├── Server/
│   ├── ServerModule.cs
│   ├── ServerSlashModule.cs
│   ├── TicketModule.cs
│   ├── RulesModule.cs
│   ├── EmbedModule.cs
│   ├── QuoteModule.cs
│   └── ReminderModule.cs
├── Fun/
│   ├── FunModule.cs
│   ├── DuelSlashModule.cs
│   └── Casino/
│       ├── CasinoSlashModule.cs
│       └── CasinoSlashModule.Games.cs
├── Utils/
│   ├── SearchModule.cs
│   ├── ConvertModule.cs
│   ├── AirportModule.cs
│   └── Weather/
│       ├── WeatherModule.cs
│       └── WeatherContainers.cs
└── Code/
    ├── CodeTipModule.cs
    ├── TipModule.cs
    └── Unity/
        └── UnityHelp/
            ├── CannedInteractiveModule.cs
            ├── CannedResponseModule.cs
            ├── GeneralHelpModule.cs
            ├── UnityHelpInteractiveModule.cs
            └── UnityHelpModule.cs
```

## Services Layout

```
Services/
├── DatabaseService.cs          (root - core)
├── CommandHandlingService.cs   (root - core)
├── LoggingService.cs           (root - core)
├── UpdateService.cs            (root - core)
├── Profiles/
│   ├── ProfileCardService.cs
│   ├── XpService.cs
│   ├── KarmaService.cs
│   ├── KarmaResetService.cs
│   ├── UserExtendedService.cs
│   └── BirthdayAnnouncementService.cs
├── Server/
│   ├── ServerService.cs
│   ├── WelcomeService.cs
│   ├── AuditLogService.cs
│   ├── EveryoneScoldService.cs
│   ├── EmbedParsingService.cs
│   └── ReminderService.cs
├── Fun/
│   ├── DuelService.cs
│   ├── MikuService.cs
│   └── Casino/
│       ├── CasinoService.cs
│       ├── GameService.cs
│       └── TransactionFormatter.cs
├── Utils/
│   ├── SearchService.cs
│   ├── AirportService.cs
│   ├── CurrencyService.cs
│   └── Weather/
│       └── WeatherService.cs
├── Code/
│   ├── CodeCheckService.cs
│   ├── Tips/
│   │   ├── TipService.cs
│   │   └── Components/
│   │       └── Tip.cs
│   └── Unity/
│       ├── UnityDocParser.cs
│       ├── ReleaseNotesParser.cs
│       ├── FeedService.cs
│       └── UnityHelp/
│           ├── CannedResponseService.cs
│           ├── UnityHelpService.cs
│           └── Components/
│               ├── HelpBotMessage.cs
│               └── ThreadContainer.cs
└── Recruitment/
    └── RecruitService.cs
```

## Namespace Strategy

New namespaces match directory paths. All new sub-namespaces added to `GlobalUsings.cs` to avoid mass-editing using statements across the project.

## Checklist

- [ ] Create directory structure
- [ ] Move Module files
- [ ] Move Service files
- [ ] Update namespace declarations in moved files
- [ ] Update GlobalUsings.cs with new namespaces
- [ ] Build and verify compilation
- [ ] Run tests
- [ ] Update documentation
