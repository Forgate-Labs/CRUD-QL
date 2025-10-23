using System;
using FluentValidation;

namespace CrudQL.Service.Entities;

public sealed record CrudValidatorRegistration(Type TargetType, IValidator Validator);
