namespace inventory_management.Models;

public class DeleteConfirmModalViewModel
{
    public string ModalId { get; set; } = "deleteModal";
    public string Title { get; set; } = "Confirm Delete";
    public string ItemNoun { get; set; } = "item";
    public string DeleteActionUrl { get; set; } = string.Empty;
    public string IdFieldName { get; set; } = "id";
    public string? Warning { get; set; }
}
