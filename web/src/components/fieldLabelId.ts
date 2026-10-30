import { createContext } from 'react';

/**
 * The id of the enclosing `Field`'s label text, for a control whose own content (like `PasswordInput`'s eye
 * button) would otherwise join its name through the wrapping label.
 */
export const FieldLabelId = createContext<string | undefined>(undefined);
