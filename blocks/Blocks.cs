/*
    Copyright (C) 2011 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks {
	public class Blocks {
		MethodDefinition method;
		IList<VariableDefinition> locals;
		MethodBlocks methodBlocks;

		public MethodBlocks MethodBlocks {
			get { return methodBlocks; }
		}

		public IList<VariableDefinition> Locals {
			get { return locals; }
		}

		public MethodDefinition Method {
			get { return method; }
		}

		public Blocks(MethodDefinition method) {
			var body = method.Body;
			this.method = method;
			this.locals = body.Variables;
			methodBlocks = new InstructionListParser(body.Instructions, body.ExceptionHandlers).parse();
		}

		public void deobfuscateLeaveObfuscation() {
			foreach (var scopeBlock in getAllScopeBlocks(methodBlocks))
				scopeBlock.deobfuscateLeaveObfuscation();
		}

		public int deobfuscate() {
			foreach (var scopeBlock in getAllScopeBlocks(methodBlocks))
				scopeBlock.deobfuscate(this);

			int numDeadBlocks = removeDeadBlocks();

			foreach (var scopeBlock in getAllScopeBlocks(methodBlocks)) {
				scopeBlock.mergeBlocks();
				scopeBlock.repartitionBlocks();
				scopeBlock.deobfuscateLeaveObfuscation();
			}

			return numDeadBlocks;
		}

		IEnumerable<ScopeBlock> getAllScopeBlocks(ScopeBlock scopeBlock) {
			var list = new List<ScopeBlock>();
			list.Add(scopeBlock);
			list.AddRange(scopeBlock.getAllScopeBlocks());
			return list;
		}

		public int removeDeadBlocks() {
			return new DeadBlocksRemover(methodBlocks).remove();
		}

		public void getCode(out IList<Instruction> allInstructions, out IList<ExceptionHandler> allExceptionHandlers) {
			new CodeGenerator(methodBlocks).getCode(out allInstructions, out allExceptionHandlers);
		}

		struct LocalVariableInfo {
			public Block block;
			public int index;
			public LocalVariableInfo(Block block, int index) {
				this.block = block;
				this.index = index;
			}
		}

		public int optimizeLocals() {
			if (locals.Count == 0)
				return 0;

			var usedLocals = new Dictionary<VariableDefinition, List<LocalVariableInfo>>();
			foreach (var block in methodBlocks.getAllBlocks()) {
				for (int i = 0; i < block.Instructions.Count; i++) {
					var instr = block.Instructions[i];
					VariableDefinition local;
					switch (instr.OpCode.Code) {
					case Code.Ldloc:
					case Code.Ldloc_S:
					case Code.Ldloc_0:
					case Code.Ldloc_1:
					case Code.Ldloc_2:
					case Code.Ldloc_3:
					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						local = Instr.getLocalVar(locals, instr);
						break;

					case Code.Ldloca_S:
					case Code.Ldloca:
						local = (VariableDefinition)instr.Operand;
						break;

					default:
						local = null;
						break;
					}
					if (local == null)
						continue;

					List<LocalVariableInfo> list;
					if (!usedLocals.TryGetValue(local, out list))
						usedLocals[local] = list = new List<LocalVariableInfo>();
					list.Add(new LocalVariableInfo(block, i));
					if (usedLocals.Count == locals.Count)
						return 0;
				}
			}

			int newIndex = -1;
			var newLocals = new List<VariableDefinition>(usedLocals.Count);
			foreach (var local in usedLocals.Keys) {
				newIndex++;
				newLocals.Add(local);
				foreach (var info in usedLocals[local])
					info.block.Instructions[info.index] = new Instr(optimizeLocalInstr(info.block.Instructions[info.index], local, (uint)newIndex));
			}

			int numRemoved = locals.Count - newLocals.Count;
			locals.Clear();
			foreach (var local in newLocals)
				locals.Add(local);
			return numRemoved;
		}

		static Instruction optimizeLocalInstr(Instr instr, VariableDefinition local, uint newIndex) {
			switch (instr.OpCode.Code) {
			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
				if (newIndex == 0)
					return Instruction.Create(OpCodes.Ldloc_0);
				if (newIndex == 1)
					return Instruction.Create(OpCodes.Ldloc_1);
				if (newIndex == 2)
					return Instruction.Create(OpCodes.Ldloc_2);
				if (newIndex == 3)
					return Instruction.Create(OpCodes.Ldloc_3);
				if (newIndex <= 0xFF)
					return Instruction.Create(OpCodes.Ldloc_S, local);
				return Instruction.Create(OpCodes.Ldloc, local);

			case Code.Stloc:
			case Code.Stloc_S:
			case Code.Stloc_0:
			case Code.Stloc_1:
			case Code.Stloc_2:
			case Code.Stloc_3:
				if (newIndex == 0)
					return Instruction.Create(OpCodes.Stloc_0);
				if (newIndex == 1)
					return Instruction.Create(OpCodes.Stloc_1);
				if (newIndex == 2)
					return Instruction.Create(OpCodes.Stloc_2);
				if (newIndex == 3)
					return Instruction.Create(OpCodes.Stloc_3);
				if (newIndex <= 0xFF)
					return Instruction.Create(OpCodes.Stloc_S, local);
				return Instruction.Create(OpCodes.Stloc, local);

			case Code.Ldloca_S:
			case Code.Ldloca:
				if (newIndex <= 0xFF)
					return Instruction.Create(OpCodes.Ldloca_S, local);
				return Instruction.Create(OpCodes.Ldloca, local);

			default:
				throw new ApplicationException("Invalid ld/st local instruction");
			}
		}

		public void repartitionBlocks() {
			foreach (var scopeBlock in getAllScopeBlocks(methodBlocks))
				scopeBlock.repartitionBlocks();
		}
	}
}
