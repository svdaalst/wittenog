namespace WitteNog.App.Models;

public enum TabType { DailyPage, TopicPage, TasksPage, OrphanPage }

public record TabViewModel(string Id, TabType Type, string Query, string Title);
